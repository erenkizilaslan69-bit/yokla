using BudgetApp.Localization;
using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetApp.Services;

/// <summary>How many receipts the user can still send today.</summary>
public sealed record ReceiptQuota(bool IsEnabled, bool IsDemo, int UsedToday, int Limit, bool TotalLimitReached)
{
    public int Remaining => IsDemo ? Limit : Math.Max(0, Limit - UsedToday);

    public bool CanScan => IsEnabled && (IsDemo || (Remaining > 0 && !TotalLimitReached));
}

/// <summary>A line on the confirmation screen.</summary>
public sealed record ReceiptReviewLine(string Name, decimal Amount, int CategoryId, string RawText, decimal? Quantity, string? Unit);

/// <summary>Everything the confirmation screen shows. Nothing of it is stored until the user saves.</summary>
/// <param name="Notes">Things the user should check (date guessed or odd, discount spread over the lines).</param>
/// <param name="DuplicateWarning">Set when a receipt with the same date, shop and total was already saved.</param>
public sealed record ReceiptReview(
    string? StoreName, DateTime Date, decimal? ReceiptTotal, decimal GeneralDiscount,
    IReadOnlyList<ReceiptReviewLine> Lines, IReadOnlyList<string> Notes, string? DuplicateWarning, bool IsDemo)
{
    public decimal LinesTotal => Lines.Sum(l => l.Amount);

    /// <summary>The lines do not add up to the printed total (beyond a few kuruş of rounding).</summary>
    public bool HasTotalMismatch => ReceiptMath.IsMismatch(ReceiptTotal, LinesTotal);
}

public sealed record ReceiptSaveLine(string Name, decimal Amount, int CategoryId);

public sealed record ReceiptSaveInput(DateTime Date, string? StoreName, IReadOnlyList<ReceiptSaveLine> Lines);

public interface IReceiptService
{
    /// <summary>False when no reader is configured (no API key outside Development): the feature is hidden.</summary>
    bool IsEnabled { get; }

    bool IsDemo { get; }

    Task<ReceiptQuota> GetQuotaAsync(string userId, DateTime now);

    /// <summary>
    /// Checks the picture and the daily limits, records the attempt and has the receipt read. The picture stays in
    /// memory. Returns what the confirmation screen needs.
    /// </summary>
    /// <exception cref="ReceiptParseException">With a message for the user.</exception>
    Task<ReceiptReview> ScanAsync(string userId, byte[] data, DateTime now, CancellationToken cancellationToken);

    /// <summary>A warning when a receipt with the same date, shop and total is already among the user's transactions.</summary>
    Task<string?> FindDuplicateAsync(string userId, DateTime date, string? storeName, decimal total);

    /// <summary>Saves the confirmed lines as separate expenses, all or none.</summary>
    /// <exception cref="ArgumentException">A line or the date is invalid (e.g. a future date).</exception>
    Task<List<Transaction>> SaveAsync(string userId, ReceiptSaveInput input);
}

public static class ReceiptMath
{
    /// <summary>Lines and total may differ by this much before the user is warned (rounding on the receipt).</summary>
    public const decimal TotalTolerance = 0.05m;

    /// <summary>True when there is a printed total and the lines differ from it by more than the tolerance.</summary>
    public static bool IsMismatch(decimal? receiptTotal, decimal linesTotal) =>
        receiptTotal is { } total && Math.Abs(total - linesTotal) > TotalTolerance;

    /// <summary>
    /// Spreads a discount on the whole receipt over its lines in proportion to their amounts, rounded down to the
    /// kuruş; what rounding leaves over is taken from the largest line. A discount as large as the receipt is
    /// not spread (it cannot be right), the amounts come back unchanged.
    /// </summary>
    public static List<decimal> DistributeDiscount(IReadOnlyList<decimal> amounts, decimal discount)
    {
        var total = amounts.Sum();
        if (discount <= 0 || total <= 0 || discount >= total)
            return amounts.ToList();

        var shares = amounts.Select(a => Math.Floor(a * discount / total * 100m) / 100m).ToList();
        var largest = amounts.Select((a, i) => (a, i)).MaxBy(x => x.a).i;
        shares[largest] += discount - shares.Sum();

        return amounts.Select((a, i) => a - shares[i]).ToList();
    }

    /// <summary>The note every saved line of a receipt gets, so the lines of one receipt can be told apart.</summary>
    public static string NoteFor(string? storeName) =>
        string.IsNullOrWhiteSpace(storeName) ? "Fiş" : $"Fiş · {Shorten(storeName.Trim(), 100)}";

    internal static string Shorten(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}

public class ReceiptService(
    ApplicationDbContext db, ITransactionService transactions, ICategoryService categories,
    IEnumerable<IReceiptParser> parsers, IOptions<ReceiptOptions> options, ILogger<ReceiptService> logger) : IReceiptService
{
    private const string FallbackCategory = "Diğer Gider";
    private readonly IReceiptParser? _parser = parsers.FirstOrDefault();
    private ReceiptOptions Settings => options.Value;

    public bool IsEnabled => _parser is not null;

    public bool IsDemo => _parser?.IsDemo == true;

    public async Task<ReceiptQuota> GetQuotaAsync(string userId, DateTime now)
    {
        if (_parser is null)
            return new ReceiptQuota(false, false, 0, Settings.DailyLimitPerUser, false);

        var (used, total) = await CountTodayAsync(userId, now);
        return new ReceiptQuota(true, _parser.IsDemo, used, Settings.DailyLimitPerUser, total >= Settings.DailyLimitTotal);
    }

    public async Task<ReceiptReview> ScanAsync(string userId, byte[] data, DateTime now, CancellationToken cancellationToken)
    {
        if (_parser is null)
            throw new ReceiptParseException(ReceiptErrorKind.Disabled);

        if (data.Length == 0 || data.Length > Settings.MaxUploadBytes || ReceiptImage.DetectMimeType(data) is not { } mimeType)
            throw new ReceiptParseException(ReceiptErrorKind.InvalidFile, $"{data.Length} bytes");

        if (!_parser.IsDemo)
        {
            var (used, total) = await CountTodayAsync(userId, now);
            if (used >= Settings.DailyLimitPerUser)
                throw new ReceiptParseException(ReceiptErrorKind.UserDailyLimit, limit: Settings.DailyLimitPerUser);
            if (total >= Settings.DailyLimitTotal)
                throw new ReceiptParseException(ReceiptErrorKind.TotalDailyLimit);
        }

        var expenseCategories = await categories.GetByTypeAsync(TransactionType.Expense);

        // Recorded before the call: an attempt uses the provider's quota even if it fails, and two quick submits
        // both count.
        var scan = new ReceiptScan { UserId = userId, CreatedAt = now, Provider = _parser.Provider, Outcome = ReceiptScanOutcome.Pending, Kind = ScanKind.Receipt };
        db.ReceiptScans.Add(scan);
        await db.SaveChangesAsync(CancellationToken.None);

        ReceiptParseResult result;
        try
        {
            result = await _parser.ParseAsync(new ReceiptImage(data, mimeType), expenseCategories.Select(c => c.Name).ToList(), cancellationToken);
        }
        catch (ReceiptParseException e)
        {
            logger.LogInformation("Receipt not read: {Kind} ({Detail})", e.Kind, e.Detail);
            await FinishAsync(scan, IsUnanswered(e.Kind) ? ReceiptScanOutcome.Unanswered : ReceiptScanOutcome.Failed);
            throw;
        }

        // Lines of 0,00 (a free bag) are not expenses: a receipt with nothing else has nothing to save.
        if (!result.IsReceipt || !result.Lines.Any(l => l.Amount > 0))
        {
            await FinishAsync(scan, ReceiptScanOutcome.Failed);
            throw new ReceiptParseException(ReceiptErrorKind.Unreadable);
        }

        await FinishAsync(scan, ReceiptScanOutcome.Succeeded);
        return await BuildReviewAsync(userId, result, expenseCategories, now);
    }

    public async Task<string?> FindDuplicateAsync(string userId, DateTime date, string? storeName, decimal total)
    {
        var note = ReceiptMath.NoteFor(storeName);
        var day = date.Date;
        var amounts = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Date >= day && t.Date < day.AddDays(1) && t.Note == note)
            .Select(t => t.Amount)
            .ToListAsync();

        if (amounts.Count == 0)
            return null;

        // Summed in memory: SQLite cannot aggregate decimal columns reliably.
        var saved = amounts.Sum();
        var shop = string.IsNullOrWhiteSpace(storeName) ? "" : $"{storeName.Trim()}, ";
        if (Math.Abs(saved - total) <= ReceiptMath.TotalTolerance)
            return $"Bu fiş zaten kaydedilmiş görünüyor ({shop}{day:dd.MM.yyyy}, {AppCulture.Money(saved)}). " +
                   "İki kez eklememek için İşlemler sayfasını kontrol et.";

        // Same day and shop but another total: maybe some lines were left out last time, maybe a second visit.
        return $"Bu tarihte bu işletmenin fişinden zaten {amounts.Count} işlem kaydedilmiş ({shop}{day:dd.MM.yyyy}, " +
               $"{AppCulture.Money(saved)}). Aynı fişse iki kez eklememek için kontrol et.";
    }

    public Task<List<Transaction>> SaveAsync(string userId, ReceiptSaveInput input)
    {
        if (input.Lines.Count == 0)
            throw new ArgumentException("Choose at least one line.", nameof(input));

        var note = ReceiptMath.NoteFor(input.StoreName);
        var lines = input.Lines.Select(line =>
        {
            var name = line.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Every line needs a name.", nameof(input));
            return new Transaction
            {
                Type = TransactionType.Expense,
                Amount = MoneyMath.Round(line.Amount),
                Date = input.Date.Date,
                CategoryId = line.CategoryId,
                ItemName = ReceiptMath.Shorten(name, 200),
                Note = note
            };
        }).ToList();

        // Same rules as a single expense: positive amount, no future date, an expense category.
        return transactions.CreateManyAsync(userId, lines);
    }

    // ---------- helpers ----------

    /// <summary>The user's receipts today and every request the app sent today (see <see cref="ReaderUsage"/>).</summary>
    private Task<(int User, int Total)> CountTodayAsync(string userId, DateTime now) =>
        ReaderUsage.CountTodayAsync(db, userId, ScanKind.Receipt, now);

    /// <summary>
    /// Errors where the reader gave no answer and the user could not have done anything differently:
    /// these attempts do not use up the user's daily allowance.
    /// </summary>
    public static bool IsUnanswered(ReceiptErrorKind kind) => kind is
        ReceiptErrorKind.Timeout or ReceiptErrorKind.ServiceUnavailable or ReceiptErrorKind.Overloaded or ReceiptErrorKind.RateLimited
        or ReceiptErrorKind.QuotaExceeded or ReceiptErrorKind.Misconfigured;

    private async Task FinishAsync(ReceiptScan scan, ReceiptScanOutcome outcome)
    {
        scan.Outcome = outcome;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<ReceiptReview> BuildReviewAsync(
        string userId, ReceiptParseResult result, List<Category> expenseCategories, DateTime now)
    {
        var notes = new List<string>();
        var today = now.Date;

        var date = today;
        if (result.Date is not { } read)
            notes.Add("Fiş tarihi okunamadı; bugünün tarihini koydum. Gerekirse düzelt.");
        else if (read.Date > today)
            notes.Add($"Fiş tarihi {read:dd.MM.yyyy} okundu, bu ileri bir tarih; bugünün tarihini koydum. Fişteki tarihi kontrol et.");
        else
        {
            date = read.Date;
            if (date < today.AddYears(-1))
                notes.Add($"Fiş tarihi {date:dd.MM.yyyy} okundu, bir yıldan eski. Doğru okunmuş mu kontrol et.");
        }

        var amounts = result.Lines.Select(l => l.Amount).ToList();
        var discount = result.GeneralDiscount;
        if (discount > 0)
        {
            if (discount < amounts.Sum())
            {
                amounts = ReceiptMath.DistributeDiscount(amounts, discount);
                notes.Add($"Fişteki {AppCulture.Money(discount)} genel indirim, tutarlarına oranla satırlara dağıtıldı.");
            }
            else
            {
                notes.Add($"Fişte {AppCulture.Money(discount)} tutarında bir indirim okundu ama satırlara dağıtılamadı; tutarları kontrol et.");
                discount = 0;
            }
        }

        var byName = expenseCategories.ToDictionary(c => c.Name, c => c.Id, StringComparer.Create(Tr.Culture, true));
        var fallback = expenseCategories.FirstOrDefault(c => c.Name == FallbackCategory)?.Id ?? expenseCategories[^1].Id;

        var lines = result.Lines
            .Select((line, i) => new ReceiptReviewLine(
                ReceiptMath.Shorten(line.Name, 200),
                amounts[i],
                line.Category is { } name && byName.TryGetValue(name, out var id) ? id : fallback,
                line.RawText, line.Quantity, line.Unit))
            .Where(line => line.Amount > 0)
            .ToList();

        var store = result.StoreName is null ? null : ReceiptMath.Shorten(result.StoreName, 100);
        var review = new ReceiptReview(store, date, result.Total, discount, lines, notes, null, _parser!.IsDemo);
        var duplicate = await FindDuplicateAsync(userId, date, store, review.LinesTotal);
        return review with { DuplicateWarning = duplicate };
    }
}
