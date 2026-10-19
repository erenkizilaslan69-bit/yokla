using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class ReceiptServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Now = new(2026, 10, 16, 14, 30, 0);
    private static readonly DateTime Today = Now.Date;
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9];

    /// <summary>A reader that returns a prepared result (or throws) and remembers what it was given.</summary>
    private sealed class StubParser(Func<ReceiptParseResult> answer, bool isDemo = false) : IReceiptParser
    {
        public string Provider => IsDemo ? "demo" : "gemini";
        public bool IsDemo { get; } = isDemo;
        public int Calls { get; private set; }
        public IReadOnlyList<string>? Categories { get; private set; }
        public ReceiptImage? Image { get; private set; }

        public Task<ReceiptParseResult> ParseAsync(ReceiptImage image, IReadOnlyList<string> expenseCategories, CancellationToken cancellationToken)
        {
            Calls++;
            Image = image;
            Categories = expenseCategories;
            return Task.FromResult(answer());
        }
    }

    private static ReceiptParseResult Receipt(DateTime? date = null, decimal? total = 76.50m, decimal discount = 0m,
        string? store = "Köşe Market", params ReceiptLine[] lines) =>
        new(true, store, date ?? Today.AddDays(-1), total, discount,
            lines.Length > 0 ? lines :
            [
                new("SUT YRM YGL 1L", "Yarım yağlı süt 1 L", 1m, "adet", 42.50m, "Market"),
                new("MAKARNA 2 X 17,00", "Makarna", 2m, "adet", 34.00m, "Market")
            ]);

    private static ReceiptService Service(TestDb t, IReceiptParser? parser, int perUser = 10, int total = 200, int maxBytes = 4_000_000) =>
        new(t.Context, new TransactionService(t.Context, new FixedTime(Now)), new CategoryService(t.Context),
            parser is null ? [] : [parser],
            Options.Create(new ReceiptOptions { DailyLimitPerUser = perUser, DailyLimitTotal = total, MaxUploadBytes = maxBytes }),
            NullLogger<ReceiptService>.Instance);

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static void AddScans(TestDb t, string user, int count, DateTime at, string provider = "gemini")
    {
        for (var i = 0; i < count; i++)
            t.Context.ReceiptScans.Add(new ReceiptScan { UserId = user, CreatedAt = at, Provider = provider, Outcome = ReceiptScanOutcome.Succeeded });
        t.Context.SaveChanges();
    }

    private static async Task<ReceiptErrorKind> FailureOf(Func<Task> act) =>
        (await Assert.ThrowsAsync<ReceiptParseException>(act)).Kind;

    // ---------- the picture ----------

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63 }, "image/heic")]
    [InlineData(new byte[] { 0, 0, 0, 24, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x69, 0x66, 0x31 }, "image/heif")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 }, null)]          // %PDF
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, null)] // GIF
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, null)] // RIFF but WAVE
    [InlineData(new byte[] { }, null)]
    public void DetectMimeType_GoesByTheFirstBytes(byte[] data, string? expected) =>
        Assert.Equal(expected, ReceiptImage.DetectMimeType(data));

    [Fact]
    public async Task ScanAsync_RefusesAFileThatIsNotAPictureOrTooLargeWithoutCallingTheReader()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => Receipt());
        var service = Service(t, parser, maxBytes: 10);

        Assert.Equal(ReceiptErrorKind.InvalidFile, await FailureOf(() => service.ScanAsync(Alice, "%PDF-1.7"u8.ToArray(), Now, default)));
        Assert.Equal(ReceiptErrorKind.InvalidFile, await FailureOf(() => service.ScanAsync(Alice, [], Now, default)));
        Assert.Equal(ReceiptErrorKind.InvalidFile, await FailureOf(() => service.ScanAsync(Alice, [0xFF, 0xD8, 0xFF, 1, 2, 3, 4, 5, 6, 7, 8], Now, default)));
        Assert.Equal(0, parser.Calls);
        Assert.Empty(t.Context.ReceiptScans);
    }

    [Fact]
    public async Task ScanAsync_HandsThePictureAndTheExpenseCategoriesToTheReader()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => Receipt());

        await Service(t, parser).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal(Jpeg, parser.Image!.Data);
        Assert.Equal("image/jpeg", parser.Image.MimeType);
        Assert.Equal(12, parser.Categories!.Count);
        Assert.Contains("Market", parser.Categories);
        Assert.DoesNotContain("Maaş", parser.Categories);
    }

    // ---------- switched off ----------

    [Fact]
    public async Task WithoutAReaderTheFeatureIsOff()
    {
        using var t = new TestDb();
        var service = Service(t, null);

        Assert.False(service.IsEnabled);
        Assert.False(service.IsDemo);
        Assert.False((await service.GetQuotaAsync(Alice, Now)).CanScan);
        Assert.Equal(ReceiptErrorKind.Disabled, await FailureOf(() => service.ScanAsync(Alice, Jpeg, Now, default)));
    }

    // ---------- daily limits ----------

    [Fact]
    public async Task ScanAsync_StopsAtTheUsersDailyLimit()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 9, Today.AddHours(9));
        var parser = new StubParser(() => Receipt());
        var service = Service(t, parser);

        await service.ScanAsync(Alice, Jpeg, Now, default); // the 10th

        var error = await Assert.ThrowsAsync<ReceiptParseException>(() => service.ScanAsync(Alice, Jpeg, Now, default));
        Assert.Equal(ReceiptErrorKind.UserDailyLimit, error.Kind);
        Assert.Contains("10/10", error.Message);
        Assert.Equal(1, parser.Calls);
    }

    [Fact]
    public async Task ScanAsync_CountsOnlyTodayAndOnlyTheUsersOwnAttempts()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 10, Today.AddSeconds(-1));   // yesterday, one second before midnight
        AddScans(t, Bob, 10, Today.AddHours(8));        // someone else today
        var service = Service(t, new StubParser(() => Receipt()));

        await service.ScanAsync(Alice, Jpeg, Now, default);

        var quota = await service.GetQuotaAsync(Alice, Now);
        Assert.Equal((1, 9), (quota.UsedToday, quota.Remaining));
    }

    [Fact]
    public async Task ScanAsync_StopsAtTheAppsDailyLimitForEveryone()
    {
        using var t = new TestDb();
        AddScans(t, Bob, 3, Today.AddHours(8));
        var service = Service(t, new StubParser(() => Receipt()), total: 3);

        Assert.Equal(ReceiptErrorKind.TotalDailyLimit, await FailureOf(() => service.ScanAsync(Alice, Jpeg, Now, default)));
        Assert.True((await service.GetQuotaAsync(Alice, Now)).TotalLimitReached);
        Assert.False((await service.GetQuotaAsync(Alice, Now)).CanScan);
    }

    [Theory]
    [InlineData(ReceiptErrorKind.Blocked)]
    [InlineData(ReceiptErrorKind.InvalidResponse)]
    [InlineData(ReceiptErrorKind.Unreadable)]
    public async Task ScanAsync_AnAnsweredButUnusableAttemptUsesTheAllowance(ReceiptErrorKind kind)
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(() => throw new ReceiptParseException(kind)));

        await FailureOf(() => service.ScanAsync(Alice, Jpeg, Now, default));

        var scan = Assert.Single(t.Context.ReceiptScans);
        Assert.Equal((Alice, "gemini", ReceiptScanOutcome.Failed, Now), (scan.UserId, scan.Provider, scan.Outcome, scan.CreatedAt));
        Assert.Equal(1, (await service.GetQuotaAsync(Alice, Now)).UsedToday);
    }

    [Theory]
    [InlineData(ReceiptErrorKind.Timeout)]
    [InlineData(ReceiptErrorKind.ServiceUnavailable)]
    [InlineData(ReceiptErrorKind.RateLimited)]
    [InlineData(ReceiptErrorKind.QuotaExceeded)]
    [InlineData(ReceiptErrorKind.Misconfigured)]
    public async Task ScanAsync_AnUnansweredAttemptDoesNotUseTheUsersAllowanceButCountsForTheApp(ReceiptErrorKind kind)
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(() => throw new ReceiptParseException(kind)), total: 1);

        await FailureOf(() => service.ScanAsync(Alice, Jpeg, Now, default));

        Assert.Equal(ReceiptScanOutcome.Unanswered, Assert.Single(t.Context.ReceiptScans).Outcome);
        var quota = await service.GetQuotaAsync(Alice, Now);
        Assert.Equal((0, 10), (quota.UsedToday, quota.Remaining));
        Assert.True(quota.TotalLimitReached); // the provider may have spent quota on it
    }

    [Fact]
    public async Task ScanAsync_TimeoutsDoNotLockTheUserOut()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(() => throw new ReceiptParseException(ReceiptErrorKind.Timeout)), perUser: 2);

        for (var i = 0; i < 5; i++)
            Assert.Equal(ReceiptErrorKind.Timeout, await FailureOf(() => service.ScanAsync(Alice, Jpeg, Now, default)));

        Assert.Equal(5, t.Context.ReceiptScans.Count());
    }

    [Fact]
    public async Task AnAttemptStillWaitingForItsAnswerCounts()
    {
        using var t = new TestDb();
        t.Context.ReceiptScans.Add(new ReceiptScan { UserId = Alice, CreatedAt = Today.AddHours(9), Provider = "gemini", Outcome = ReceiptScanOutcome.Pending });
        t.Context.SaveChanges();

        Assert.Equal(1, (await Service(t, new StubParser(() => Receipt())).GetQuotaAsync(Alice, Now)).UsedToday);
    }

    [Theory]
    [InlineData(ReceiptErrorKind.Timeout, true)]
    [InlineData(ReceiptErrorKind.ServiceUnavailable, true)]
    [InlineData(ReceiptErrorKind.Overloaded, true)]
    [InlineData(ReceiptErrorKind.RateLimited, true)]
    [InlineData(ReceiptErrorKind.QuotaExceeded, true)]
    [InlineData(ReceiptErrorKind.Misconfigured, true)]
    [InlineData(ReceiptErrorKind.Blocked, false)]
    [InlineData(ReceiptErrorKind.InvalidResponse, false)]
    [InlineData(ReceiptErrorKind.Unreadable, false)]
    [InlineData(ReceiptErrorKind.InvalidFile, false)]
    [InlineData(ReceiptErrorKind.UserDailyLimit, false)]
    public void IsUnanswered_IsTrueOnlyWhenTheReaderGaveNoAnswer(ReceiptErrorKind kind, bool expected) =>
        Assert.Equal(expected, ReceiptService.IsUnanswered(kind));

    [Fact]
    public async Task ScanAsync_RecordsASuccessfulAttemptWithoutThePictureOrTheResult()
    {
        using var t = new TestDb();

        await Service(t, new StubParser(() => Receipt())).ScanAsync(Alice, Jpeg, Now, default);

        var scan = Assert.Single(t.Context.ReceiptScans);
        Assert.Equal(ReceiptScanOutcome.Succeeded, scan.Outcome);
        Assert.Empty(t.Context.Transactions); // nothing is saved before the user confirms
    }

    [Fact]
    public async Task ADemoReaderHasNoLimits()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 50, Today.AddHours(8), provider: "demo");
        var service = Service(t, new StubParser(() => Receipt(), isDemo: true), perUser: 1, total: 1);

        var review = await service.ScanAsync(Alice, Jpeg, Now, default);
        var quota = await service.GetQuotaAsync(Alice, Now);

        Assert.True(review.IsDemo);
        Assert.True(service.IsDemo);
        Assert.True(quota.CanScan);
        Assert.Equal(0, quota.UsedToday); // demo attempts are not counted
    }

    // ---------- the confirmation screen ----------

    [Fact]
    public async Task ScanAsync_ReturnsTheLinesWithCategoryIdsDateStoreAndTotal()
    {
        using var t = new TestDb();

        var review = await Service(t, new StubParser(() => Receipt())).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal("Köşe Market", review.StoreName);
        Assert.Equal(Today.AddDays(-1), review.Date);
        Assert.Equal(76.50m, review.ReceiptTotal);
        Assert.Equal(76.50m, review.LinesTotal);
        Assert.False(review.HasTotalMismatch);
        Assert.Empty(review.Notes);
        Assert.Null(review.DuplicateWarning);
        Assert.Equal(["Yarım yağlı süt 1 L", "Makarna"], review.Lines.Select(l => l.Name));
        Assert.All(review.Lines, l => Assert.Equal(Market, l.CategoryId));
        Assert.Equal((2m, "adet", "MAKARNA 2 X 17,00"), (review.Lines[1].Quantity, review.Lines[1].Unit, review.Lines[1].RawText));
    }

    [Fact]
    public async Task ScanAsync_AnUnknownOrMissingCategoryBecomesOtherExpenses()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => Receipt(lines:
        [
            new("A", "A", null, null, 10m, "Uydurma"),
            new("B", "B", null, null, 10m, null),
            new("C", "C", null, null, 10m, "kafe/kahve") // case does not matter
        ]));

        var review = await Service(t, parser).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal([13, 13, Coffee], review.Lines.Select(l => l.CategoryId)); // 13 = Diğer Gider
    }

    [Fact]
    public async Task ScanAsync_FlagsLinesThatDoNotAddUpToTheTotal()
    {
        using var t = new TestDb();

        var review = await Service(t, new StubParser(() => Receipt(total: 86.50m))).ScanAsync(Alice, Jpeg, Now, default);

        Assert.True(review.HasTotalMismatch);
        Assert.Equal(76.50m, review.LinesTotal);
    }

    [Theory]
    [InlineData("76.55", false)] // five kuruş of rounding is fine
    [InlineData("76.56", true)]
    public async Task ScanAsync_ToleratesAFewKurusOfRounding(string total, bool mismatch)
    {
        using var t = new TestDb();
        var printed = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture);

        var review = await Service(t, new StubParser(() => Receipt(total: printed))).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal(mismatch, review.HasTotalMismatch);
    }

    [Fact]
    public async Task ScanAsync_WithoutAPrintedTotalThereIsNothingToCompare()
    {
        using var t = new TestDb();

        var review = await Service(t, new StubParser(() => Receipt(total: null))).ScanAsync(Alice, Jpeg, Now, default);

        Assert.False(review.HasTotalMismatch);
    }

    [Fact]
    public async Task ScanAsync_SpreadsAGeneralDiscountOverTheLinesAndSaysSo()
    {
        using var t = new TestDb();
        // 100 + 300 with 40 off the whole receipt: 10 and 30 off the lines
        var parser = new StubParser(() => Receipt(total: 360m, discount: 40m, lines:
        [
            new("A", "A", null, null, 100m, "Market"),
            new("B", "B", null, null, 300m, "Market")
        ]));

        var review = await Service(t, parser).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal([90m, 270m], review.Lines.Select(l => l.Amount));
        Assert.Equal(40m, review.GeneralDiscount);
        Assert.False(review.HasTotalMismatch);
        Assert.Contains(review.Notes, n => n.Contains("₺40,00 genel indirim") && n.Contains("oranla"));
    }

    [Fact]
    public async Task ScanAsync_DoesNotSpreadADiscountAsLargeAsTheReceipt()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => Receipt(total: 0m, discount: 76.50m));

        var review = await Service(t, parser).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal([42.50m, 34.00m], review.Lines.Select(l => l.Amount));
        Assert.Equal(0m, review.GeneralDiscount);
        Assert.Contains(review.Notes, n => n.Contains("dağıtılamadı"));
    }

    [Fact]
    public async Task ScanAsync_UsesTodayWhenTheDateIsMissingOrInTheFuture()
    {
        using var t = new TestDb();

        var missing = await Service(t, new StubParser(() => Receipt() with { Date = null })).ScanAsync(Alice, Jpeg, Now, default);
        var future = await Service(t, new StubParser(() => Receipt(date: Today.AddDays(3)))).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal(Today, missing.Date);
        Assert.Contains(missing.Notes, n => n.Contains("okunamadı"));
        Assert.Equal(Today, future.Date);
        Assert.Contains(future.Notes, n => n.Contains("19.10.2026") && n.Contains("ileri bir tarih"));
    }

    [Fact]
    public async Task ScanAsync_KeepsAnOldDateButAsksToCheckIt()
    {
        using var t = new TestDb();

        var review = await Service(t, new StubParser(() => Receipt(date: new DateTime(2025, 3, 1)))).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal(new DateTime(2025, 3, 1), review.Date);
        Assert.Contains(review.Notes, n => n.Contains("01.03.2025") && n.Contains("bir yıldan eski"));
    }

    [Fact]
    public async Task ScanAsync_APictureWithoutAReceiptIsUnreadable()
    {
        using var t = new TestDb();
        var notAReceipt = new StubParser(() => new ReceiptParseResult(false, null, null, null, 0m, []));
        var noLines = new StubParser(() => new ReceiptParseResult(true, "X", Today, 10m, 0m, []));
        var onlyFree = new StubParser(() => Receipt(lines: [new("POSET", "Poşet", 1m, "adet", 0m, "Market")]));

        Assert.Equal(ReceiptErrorKind.Unreadable, await FailureOf(() => Service(t, notAReceipt).ScanAsync(Alice, Jpeg, Now, default)));
        Assert.Equal(ReceiptErrorKind.Unreadable, await FailureOf(() => Service(t, noLines).ScanAsync(Alice, Jpeg, Now, default)));
        Assert.Equal(ReceiptErrorKind.Unreadable, await FailureOf(() => Service(t, onlyFree).ScanAsync(Alice, Jpeg, Now, default)));
        Assert.All(t.Context.ReceiptScans, s => Assert.Equal(ReceiptScanOutcome.Failed, s.Outcome));
    }

    [Fact]
    public async Task ScanAsync_DropsFreeLines()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => Receipt(lines:
        [
            new("POSET", "Poşet", 1m, "adet", 0m, "Market"),
            new("SUT", "Süt", 1m, "adet", 42.50m, "Market")
        ]));

        var review = await Service(t, parser).ScanAsync(Alice, Jpeg, Now, default);

        Assert.Equal(["Süt"], review.Lines.Select(l => l.Name));
    }

    // ---------- the same receipt twice ----------

    [Fact]
    public async Task ScanAsync_WarnsWhenTheSameReceiptWasAlreadySaved()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(() => Receipt()));
        var first = await service.ScanAsync(Alice, Jpeg, Now, default);
        await service.SaveAsync(Alice, new ReceiptSaveInput(first.Date, first.StoreName,
            first.Lines.Select(l => new ReceiptSaveLine(l.Name, l.Amount, l.CategoryId)).ToList()));

        var second = await service.ScanAsync(Alice, Jpeg, Now, default);

        Assert.NotNull(second.DuplicateWarning);
        Assert.Contains("zaten kaydedilmiş", second.DuplicateWarning);
        Assert.Contains("Köşe Market, 15.10.2026, ₺76,50", second.DuplicateWarning);
    }

    [Fact]
    public async Task FindDuplicateAsync_NeedsTheSameDayShopAndTotalOfTheSameUser()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(() => Receipt()));
        var day = Today.AddDays(-1);
        await service.SaveAsync(Alice, new ReceiptSaveInput(day, "Köşe Market",
            [new ReceiptSaveLine("Süt", 42.50m, Market), new ReceiptSaveLine("Makarna", 34m, Market)]));

        Assert.Contains("zaten kaydedilmiş görünüyor", await service.FindDuplicateAsync(Alice, day, "Köşe Market", 76.50m));
        Assert.Contains("zaten kaydedilmiş görünüyor", await service.FindDuplicateAsync(Alice, day, "Köşe Market", 76.53m)); // rounding
        Assert.Null(await service.FindDuplicateAsync(Alice, day.AddDays(-1), "Köşe Market", 76.50m));
        Assert.Null(await service.FindDuplicateAsync(Alice, day, "Başka Market", 76.50m));
        Assert.Null(await service.FindDuplicateAsync(Bob, day, "Köşe Market", 76.50m));
    }

    [Fact]
    public async Task FindDuplicateAsync_WarnsMoreSoftlyWhenTheSameShopsReceiptWasSavedThatDayWithAnotherTotal()
    {
        using var t = new TestDb();
        var service = Service(t, null);
        // last time the detergent was left out
        await service.SaveAsync(Alice, new ReceiptSaveInput(Today, "Köşe Market",
            [new ReceiptSaveLine("Süt", 42.50m, Market), new ReceiptSaveLine("Makarna", 34m, Market)]));

        var warning = await service.FindDuplicateAsync(Alice, Today, "Köşe Market", 141.40m);

        Assert.Equal("Bu tarihte bu işletmenin fişinden zaten 2 işlem kaydedilmiş (Köşe Market, 16.10.2026, ₺76,50). " +
                     "Aynı fişse iki kez eklememek için kontrol et.", warning);
    }

    [Fact]
    public async Task FindDuplicateAsync_IgnoresExpensesEnteredByHand()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 76.50m, Today, Market, "Market alışverişi");

        Assert.Null(await Service(t, null).FindDuplicateAsync(Alice, Today, "Köşe Market", 76.50m));
    }

    // ---------- saving ----------

    [Fact]
    public async Task SaveAsync_SavesEveryLineAsItsOwnExpenseWithTheShopInTheNote()
    {
        using var t = new TestDb();

        var saved = await Service(t, null).SaveAsync(Alice, new ReceiptSaveInput(Today.AddDays(-2), " Köşe Market ",
            [new ReceiptSaveLine(" Yarım yağlı süt 1 L ", 42.504m, Market), new ReceiptSaveLine("Latte", 95m, Coffee)]));

        Assert.Equal(2, saved.Count);
        var rows = await t.Context.Transactions.OrderBy(x => x.Id).ToListAsync();
        Assert.All(rows, r =>
        {
            Assert.Equal(Alice, r.UserId);
            Assert.Equal(TransactionType.Expense, r.Type);
            Assert.Equal(Today.AddDays(-2), r.Date);
            Assert.Equal("Fiş · Köşe Market", r.Note);
        });
        Assert.Equal(("Yarım yağlı süt 1 L", 42.50m, Market), (rows[0].ItemName, rows[0].Amount, rows[0].CategoryId));
        Assert.Equal(("Latte", 95m, Coffee), (rows[1].ItemName, rows[1].Amount, rows[1].CategoryId));
    }

    [Fact]
    public async Task SaveAsync_RefusesAFutureDate()
    {
        using var t = new TestDb();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(t, null).SaveAsync(Alice,
            new ReceiptSaveInput(Today.AddDays(1), "X", [new ReceiptSaveLine("Süt", 10m, Market)])));
        Assert.Empty(t.Context.Transactions);
    }

    [Fact]
    public async Task SaveAsync_SavesNothingWhenOneLineIsInvalid()
    {
        using var t = new TestDb();
        var service = Service(t, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, new ReceiptSaveInput(Today, "X",
            [new ReceiptSaveLine("Süt", 10m, Market), new ReceiptSaveLine("Maaş?", 10m, TestData.Income)])));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, new ReceiptSaveInput(Today, "X",
            [new ReceiptSaveLine("Süt", 10m, Market), new ReceiptSaveLine("Bedava", 0m, Market)])));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, new ReceiptSaveInput(Today, "X",
            [new ReceiptSaveLine("Süt", 10m, Market), new ReceiptSaveLine("  ", 5m, Market)])));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, new ReceiptSaveInput(Today, "X", [])));

        Assert.Empty(t.Context.Transactions);
    }

    [Fact]
    public async Task SaveAsync_ShortensVeryLongNames()
    {
        using var t = new TestDb();

        await Service(t, null).SaveAsync(Alice, new ReceiptSaveInput(Today, new string('M', 300),
            [new ReceiptSaveLine(new string('A', 250), 10m, Market)]));

        var row = await t.Context.Transactions.SingleAsync();
        Assert.Equal(200, row.ItemName!.Length);
        Assert.Equal("Fiş · " + new string('M', 100), row.Note);
    }

    // ---------- arithmetic ----------

    [Fact]
    public void DistributeDiscount_SplitsInProportionAndGivesTheRoundingRestToTheLargestLine()
    {
        // 10 off 33,33 + 33,33 + 33,34: 3,33 + 3,33 + 3,34
        Assert.Equal([30.00m, 30.00m, 30.00m], ReceiptMath.DistributeDiscount([33.33m, 33.33m, 33.34m], 10m));
        Assert.Equal([90m, 270m], ReceiptMath.DistributeDiscount([100m, 300m], 40m));

        var odd = ReceiptMath.DistributeDiscount([19.99m, 5.01m, 75.00m], 7.77m);
        Assert.Equal(100m - 7.77m, odd.Sum());
        Assert.All(odd, a => Assert.Equal(a, Math.Round(a, 2)));
    }

    [Fact]
    public void DistributeDiscount_LeavesTheAmountsAloneWithoutADiscountOrWithAnImpossibleOne()
    {
        Assert.Equal([10m, 20m], ReceiptMath.DistributeDiscount([10m, 20m], 0m));
        Assert.Equal([10m, 20m], ReceiptMath.DistributeDiscount([10m, 20m], -5m));
        Assert.Equal([10m, 20m], ReceiptMath.DistributeDiscount([10m, 20m], 30m));
        Assert.Empty(ReceiptMath.DistributeDiscount([], 5m));
    }

    [Fact]
    public void NoteFor_NamesTheShop()
    {
        Assert.Equal("Fiş · Köşe Market", ReceiptMath.NoteFor("  Köşe Market "));
        Assert.Equal("Fiş", ReceiptMath.NoteFor(null));
        Assert.Equal("Fiş", ReceiptMath.NoteFor(" "));
    }

    [Fact]
    public void Quota_RemainingAndCanScan()
    {
        Assert.Equal((7, true), (new ReceiptQuota(true, false, 3, 10, false).Remaining, new ReceiptQuota(true, false, 3, 10, false).CanScan));
        Assert.False(new ReceiptQuota(true, false, 10, 10, false).CanScan);
        Assert.False(new ReceiptQuota(true, false, 0, 10, true).CanScan);
        Assert.False(new ReceiptQuota(false, false, 0, 10, false).CanScan);
        Assert.True(new ReceiptQuota(true, true, 99, 10, true).CanScan);
    }
}
