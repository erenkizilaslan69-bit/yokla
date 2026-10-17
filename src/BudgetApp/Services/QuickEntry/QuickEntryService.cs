using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BudgetApp.Services;

/// <summary>How many quick entries the user can still send today.</summary>
public sealed record QuickEntryQuota(bool IsEnabled, bool IsDemo, int UsedToday, int Limit, bool TotalLimitReached)
{
    public int Remaining => IsDemo ? Limit : Math.Max(0, Limit - UsedToday);

    public bool CanUse => IsEnabled && (IsDemo || (Remaining > 0 && !TotalLimitReached));
}

/// <summary>A line the user confirmed (or that was clear enough to save without asking).</summary>
public sealed record QuickEntrySaveLine(string Description, decimal Amount, TransactionType Type, int CategoryId, DateTime Date);

public interface IQuickEntryService
{
    /// <summary>False when no reader is configured (no API key outside Development): the feature is hidden.</summary>
    bool IsEnabled { get; }

    bool IsDemo { get; }

    Task<QuickEntryQuota> GetQuotaAsync(string userId, DateTime now);

    /// <summary>
    /// Checks the input and the daily limits, records the attempt and has the sentence split into lines. The recording
    /// stays in memory. Nothing is saved here: the proposal says whether the lines may be saved without asking.
    /// </summary>
    /// <exception cref="QuickEntryException">With a message for the user.</exception>
    Task<QuickEntryProposal> ParseAsync(string userId, QuickEntryInput input, DateTime now, CancellationToken cancellationToken);

    /// <summary>Saves the lines as transactions, all or none, with the same rules as any transaction.</summary>
    /// <exception cref="ArgumentException">A line is invalid (no name, amount 0, future date, wrong category).</exception>
    Task<List<Transaction>> SaveAsync(string userId, IReadOnlyList<QuickEntrySaveLine> lines);

    /// <summary>The user's quick entry transactions among the ids, for "Düzenle".</summary>
    Task<List<Transaction>> GetSavedAsync(string userId, IReadOnlyCollection<int> ids);

    /// <summary>"Geri al": deletes the user's quick entry transactions among the ids. Returns how many.</summary>
    Task<int> UndoAsync(string userId, IReadOnlyCollection<int> ids);

    /// <summary>"Düzenle" saved: the user's quick entry transactions among the ids are replaced by the lines, in one go.</summary>
    /// <exception cref="ArgumentException">A line is invalid; nothing changes.</exception>
    Task<List<Transaction>> ReplaceAsync(string userId, IReadOnlyCollection<int> ids, IReadOnlyList<QuickEntrySaveLine> lines);
}

public class QuickEntryService(
    ApplicationDbContext db, ITransactionService transactions, ICategoryService categories,
    IEnumerable<IQuickEntryParser> parsers, IOptions<QuickEntryOptions> options, IOptions<ReceiptOptions> receiptOptions,
    ILogger<QuickEntryService> logger) : IQuickEntryService
{
    private readonly IQuickEntryParser? _parser = parsers.FirstOrDefault();
    private QuickEntryOptions Settings => options.Value;

    /// <summary>The app-wide daily limit, shared with receipt reading.</summary>
    private int TotalLimit => receiptOptions.Value.DailyLimitTotal;

    public bool IsEnabled => _parser is not null;

    public bool IsDemo => _parser?.IsDemo == true;

    public async Task<QuickEntryQuota> GetQuotaAsync(string userId, DateTime now)
    {
        if (_parser is null)
            return new QuickEntryQuota(false, false, 0, Settings.DailyLimitPerUser, false);

        var (used, total) = await ReaderUsage.CountTodayAsync(db, userId, ScanKind.QuickEntry, now);
        return new QuickEntryQuota(true, _parser.IsDemo, used, Settings.DailyLimitPerUser, total >= TotalLimit);
    }

    public async Task<QuickEntryProposal> ParseAsync(string userId, QuickEntryInput input, DateTime now, CancellationToken cancellationToken)
    {
        if (_parser is null)
            throw new QuickEntryException(QuickEntryErrorKind.Disabled);

        input = Checked(input);

        if (!_parser.IsDemo)
        {
            var (used, total) = await ReaderUsage.CountTodayAsync(db, userId, ScanKind.QuickEntry, now);
            if (used >= Settings.DailyLimitPerUser)
                throw new QuickEntryException(QuickEntryErrorKind.UserDailyLimit, limit: Settings.DailyLimitPerUser);
            if (total >= TotalLimit)
                throw new QuickEntryException(QuickEntryErrorKind.TotalDailyLimit);
        }

        var all = await categories.GetAllAsync();
        var context = new QuickEntryContext(now.Date,
            all.Where(c => c.Type == TransactionType.Expense).Select(c => c.Name).ToList(),
            all.Where(c => c.Type == TransactionType.Income).Select(c => c.Name).ToList());

        // Recorded before the call: an attempt uses the provider's quota even if it fails, and two quick submits
        // both count.
        var scan = new ReceiptScan
        {
            UserId = userId, CreatedAt = now, Provider = _parser.Provider, Outcome = ReceiptScanOutcome.Pending, Kind = ScanKind.QuickEntry
        };
        db.ReceiptScans.Add(scan);
        await db.SaveChangesAsync(CancellationToken.None);

        QuickEntryParseResult result;
        try
        {
            result = await _parser.ParseAsync(input, context, cancellationToken);
        }
        catch (QuickEntryException e)
        {
            logger.LogInformation("Quick entry not read: {Kind} ({Detail})", e.Kind, e.Detail);
            await FinishAsync(scan, IsUnanswered(e.Kind) ? ReceiptScanOutcome.Unanswered : ReceiptScanOutcome.Failed);
            throw;
        }

        // The reader answered but heard no money: that still used an attempt.
        if (!result.IsEntry || result.Items.Count == 0)
        {
            await FinishAsync(scan, ReceiptScanOutcome.Failed);
            throw new QuickEntryException(QuickEntryErrorKind.NotUnderstood);
        }

        await FinishAsync(scan, ReceiptScanOutcome.Succeeded);

        // For typed text the user's own words are what they "said".
        var heard = input.Text?.Trim() ?? result.Heard.Trim();
        var lines = QuickEntryRules.Resolve(result.Items, all, now.Date);
        var autoSave = QuickEntryRules.CanAutoSave(heard, result.Items, lines, now.Date);
        logger.LogInformation("Quick entry read: {Lines} lines, {Decision}.", lines.Count, autoSave ? "saved without asking" : "sent to confirmation");
        return new QuickEntryProposal(heard, lines, autoSave, _parser.IsDemo);
    }

    public Task<List<Transaction>> SaveAsync(string userId, IReadOnlyList<QuickEntrySaveLine> lines) =>
        transactions.CreateManyAsync(userId, ToTransactions(lines));

    public Task<List<Transaction>> GetSavedAsync(string userId, IReadOnlyCollection<int> ids) =>
        Saved(userId, ids).AsNoTracking().OrderBy(t => t.Id).ToListAsync();

    public async Task<int> UndoAsync(string userId, IReadOnlyCollection<int> ids) =>
        await transactions.DeleteManyAsync(userId, await Saved(userId, ids).Select(t => t.Id).ToListAsync());

    public async Task<List<Transaction>> ReplaceAsync(string userId, IReadOnlyCollection<int> ids, IReadOnlyList<QuickEntrySaveLine> lines) =>
        await transactions.ReplaceManyAsync(userId, await Saved(userId, ids).Select(t => t.Id).ToListAsync(), ToTransactions(lines));

    /// <summary>
    /// Errors where the reader gave no answer and the user could not have done anything differently:
    /// these attempts do not use up the user's daily allowance.
    /// </summary>
    public static bool IsUnanswered(QuickEntryErrorKind kind) => kind is
        QuickEntryErrorKind.Timeout or QuickEntryErrorKind.ServiceUnavailable or QuickEntryErrorKind.RateLimited
        or QuickEntryErrorKind.QuotaExceeded or QuickEntryErrorKind.Misconfigured;

    // ---------- helpers ----------

    /// <summary>The input with typed text trimmed and a recording's type taken from its bytes.</summary>
    private QuickEntryInput Checked(QuickEntryInput input)
    {
        if (input.Audio is { } audio)
        {
            if (audio.Data.Length == 0 || audio.Data.Length > Settings.MaxAudioBytes
                || QuickEntryAudio.DetectMimeType(audio.Data) is not { } mimeType)
                throw new QuickEntryException(QuickEntryErrorKind.InvalidAudio, $"{audio.Data.Length} bytes");
            return QuickEntryInput.FromAudio(audio with { MimeType = mimeType });
        }

        var text = input.Text?.Trim() ?? "";
        if (text.Length == 0)
            throw new QuickEntryException(QuickEntryErrorKind.Empty);
        if (text.Length > Settings.MaxTextLength)
            throw new QuickEntryException(QuickEntryErrorKind.TextTooLong, limit: Settings.MaxTextLength);
        return QuickEntryInput.FromText(text);
    }

    /// <summary>The user's transactions among the ids that came from quick entry; nothing else is touched.</summary>
    private IQueryable<Transaction> Saved(string userId, IReadOnlyCollection<int> ids) =>
        db.Transactions.Where(t => t.UserId == userId && ids.Contains(t.Id) && t.Note == QuickEntryRules.Note);

    private static List<Transaction> ToTransactions(IReadOnlyList<QuickEntrySaveLine> lines)
    {
        if (lines.Count == 0)
            throw new ArgumentException("Choose at least one line.", nameof(lines));

        return lines.Select(line =>
        {
            var name = line.Description?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Every line needs a name.", nameof(lines));
            return new Transaction
            {
                Type = line.Type,
                Amount = Math.Round(line.Amount, 2, MidpointRounding.AwayFromZero),
                Date = line.Date.Date,
                CategoryId = line.CategoryId,
                ItemName = ReceiptMath.Shorten(name, 200),
                Note = QuickEntryRules.Note
            };
        }).ToList();
    }

    private async Task FinishAsync(ReceiptScan scan, ReceiptScanOutcome outcome)
    {
        scan.Outcome = outcome;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
