using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class QuickEntryServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Now = new(2026, 10, 18, 9, 15, 0);
    private static readonly DateTime Today = Now.Date;
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 9, 9, 9];

    /// <summary>A reader that returns a prepared result (or throws) and remembers what it was given.</summary>
    private sealed class StubParser(Func<QuickEntryParseResult> answer, bool isDemo = false) : IQuickEntryParser
    {
        public string Provider => IsDemo ? "demo" : "gemini";
        public bool IsDemo { get; } = isDemo;
        public int Calls { get; private set; }
        public QuickEntryInput? Input { get; private set; }
        public QuickEntryContext? Context { get; private set; }

        public Task<QuickEntryParseResult> ParseAsync(QuickEntryInput input, QuickEntryContext context, CancellationToken cancellationToken)
        {
            Calls++;
            (Input, Context) = (input, context);
            return Task.FromResult(answer());
        }
    }

    private static QuickEntryItem Item(string description, decimal? amount, string category,
        TransactionType type = TransactionType.Expense, QuickEntryConfidence confidence = QuickEntryConfidence.High) =>
        new(description, amount, type, category, null, confidence, null);

    private static QuickEntryParseResult Clear() => new("Simitçiye 20, otobüse 15.", true,
        [Item("Simit", 20m, "Dışarıda Yemek"), Item("Otobüs", 15m, "Ulaşım")]);

    private static QuickEntryService Service(TestDb t, IQuickEntryParser? parser, int perUser = 30, int total = 300,
        int maxAudio = 2_000_000, int maxText = 500) =>
        new(t.Context, new TransactionService(t.Context, new FixedTime(Now)), new CategoryService(t.Context),
            parser is null ? [] : [parser],
            Options.Create(new QuickEntryOptions { DailyLimitPerUser = perUser, MaxAudioBytes = maxAudio, MaxTextLength = maxText }),
            Options.Create(new ReceiptOptions { DailyLimitTotal = total }),
            NullLogger<QuickEntryService>.Instance);

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static void AddScans(TestDb t, string user, int count, ScanKind kind, string provider = "gemini",
        ReceiptScanOutcome outcome = ReceiptScanOutcome.Succeeded)
    {
        for (var i = 0; i < count; i++)
            t.Context.ReceiptScans.Add(new ReceiptScan { UserId = user, CreatedAt = Now.AddMinutes(-i), Provider = provider, Outcome = outcome, Kind = kind });
        t.Context.SaveChanges();
    }

    private static Task<QuickEntryProposal> ParseText(QuickEntryService service, string text = "simitçiye 20, otobüse 15") =>
        service.ParseAsync(Alice, QuickEntryInput.FromText(text), Now, default);

    private static async Task<QuickEntryErrorKind> FailureOf(Func<Task> action) =>
        (await Assert.ThrowsAsync<QuickEntryException>(action)).Kind;

    // ---------- reading ----------

    [Fact]
    public async Task ParseAsync_ClearLinesMayBeSavedWithoutAsking()
    {
        using var t = new TestDb();
        var parser = new StubParser(Clear);

        var proposal = await ParseText(Service(t, parser));

        Assert.True(proposal.AutoSave);
        Assert.Equal([(EatingOut, 20m), (Transport, 15m)], proposal.Lines.Select(l => (l.CategoryId, l.Amount!.Value)));
        Assert.Empty(t.Context.Transactions); // nothing is saved while reading
    }

    [Fact]
    public async Task ParseAsync_ADoubtfulLineSendsThemToConfirmation()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => new("x", true, [Item("Simit", 20m, "Dışarıda Yemek"), Item("Otobüs", null, "Ulaşım")]));

        var proposal = await ParseText(Service(t, parser));

        Assert.False(proposal.AutoSave);
        Assert.NotNull(proposal.Lines[1].Problem);
    }

    [Fact]
    public async Task ParseAsync_GivesTheReaderTodayAndBothKindsOfCategories()
    {
        using var t = new TestDb();
        var parser = new StubParser(Clear);

        await ParseText(Service(t, parser));

        Assert.Equal(Today, parser.Context!.Today);
        Assert.Contains("Market", parser.Context.ExpenseCategories);
        Assert.Contains("KYK Bursu", parser.Context.IncomeCategories);
        Assert.DoesNotContain("KYK Bursu", parser.Context.ExpenseCategories);
    }

    [Fact]
    public async Task ParseAsync_ForTypedTextShowsTheUsersOwnWords()
    {
        using var t = new TestDb();

        var proposal = await ParseText(Service(t, new StubParser(Clear)), "  simitçiye 20, otobüse 15 ");

        Assert.Equal("simitçiye 20, otobüse 15", proposal.Heard);
    }

    [Fact]
    public async Task ParseAsync_ForARecordingShowsWhatWasHeardAndSendsTheTypeFromItsBytes()
    {
        using var t = new TestDb();
        var parser = new StubParser(Clear);

        var proposal = await Service(t, parser).ParseAsync(Alice, QuickEntryInput.FromAudio(new QuickEntryAudio(Webm, "audio/mp4")), Now, default);

        Assert.Equal("Simitçiye 20, otobüse 15.", proposal.Heard);
        Assert.Equal("audio/webm", parser.Input!.Audio!.MimeType);
        Assert.Null(parser.Input.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ParseAsync_EmptyTextIsRefusedWithoutCallingTheReader(string text)
    {
        using var t = new TestDb();
        var parser = new StubParser(Clear);

        Assert.Equal(QuickEntryErrorKind.Empty, await FailureOf(() => ParseText(Service(t, parser), text)));
        Assert.Equal(0, parser.Calls);
        Assert.Empty(t.Context.ReceiptScans);
    }

    [Fact]
    public async Task ParseAsync_TooLongTextIsRefused()
    {
        using var t = new TestDb();
        var parser = new StubParser(Clear);

        var e = await Assert.ThrowsAsync<QuickEntryException>(() => ParseText(Service(t, parser, maxText: 10), "simitçiye 20 lira"));

        Assert.Equal(QuickEntryErrorKind.TextTooLong, e.Kind);
        Assert.Contains("10 karakter", e.Message);
        Assert.Equal(0, parser.Calls);
    }

    [Fact]
    public async Task ParseAsync_ARecordingThatIsNotAudioOrTooLargeIsRefused()
    {
        using var t = new TestDb();
        var parser = new StubParser(Clear);
        var service = Service(t, parser, maxAudio: 6);

        Task Send(byte[] data) => service.ParseAsync(Alice, QuickEntryInput.FromAudio(new QuickEntryAudio(data, "audio/webm")), Now, default);

        Assert.Equal(QuickEntryErrorKind.InvalidAudio, await FailureOf(() => Send([])));
        Assert.Equal(QuickEntryErrorKind.InvalidAudio, await FailureOf(() => Send([0xFF, 0xD8, 0xFF])));
        Assert.Equal(QuickEntryErrorKind.InvalidAudio, await FailureOf(() => Send(Webm))); // 7 bytes > 6
        Assert.Equal(0, parser.Calls);
    }

    [Fact]
    public async Task ParseAsync_NoMoneyHeardUsesAnAttempt()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => new("", false, []));

        Assert.Equal(QuickEntryErrorKind.NotUnderstood, await FailureOf(() => ParseText(Service(t, parser))));

        var scan = Assert.Single(t.Context.ReceiptScans);
        Assert.Equal((ReceiptScanOutcome.Failed, ScanKind.QuickEntry), (scan.Outcome, scan.Kind));
        Assert.Equal(1, (await Service(t, parser).GetQuotaAsync(Alice, Now)).UsedToday);
    }

    [Fact]
    public async Task ParseAsync_AReaderThatNeverAnsweredDoesNotUseAnAttempt()
    {
        using var t = new TestDb();
        var parser = new StubParser(() => throw new QuickEntryException(QuickEntryErrorKind.Timeout));

        Assert.Equal(QuickEntryErrorKind.Timeout, await FailureOf(() => ParseText(Service(t, parser))));

        Assert.Equal(ReceiptScanOutcome.Unanswered, Assert.Single(t.Context.ReceiptScans).Outcome);
        Assert.Equal(0, (await Service(t, parser).GetQuotaAsync(Alice, Now)).UsedToday);
    }

    [Fact]
    public async Task ParseAsync_RecordsASuccessfulAttempt()
    {
        using var t = new TestDb();

        await ParseText(Service(t, new StubParser(Clear)));

        var scan = Assert.Single(t.Context.ReceiptScans);
        Assert.Equal((Alice, Now, "gemini", ReceiptScanOutcome.Succeeded, ScanKind.QuickEntry),
            (scan.UserId, scan.CreatedAt, scan.Provider, scan.Outcome, scan.Kind));
    }

    [Fact]
    public async Task ParseAsync_WithoutAReaderIsDisabled()
    {
        using var t = new TestDb();
        var service = Service(t, null);

        Assert.False(service.IsEnabled);
        Assert.Equal(QuickEntryErrorKind.Disabled, await FailureOf(() => ParseText(service)));
    }

    // ---------- limits ----------

    [Fact]
    public async Task ParseAsync_StopsAtTheUsersOwnDailyLimit()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 3, ScanKind.QuickEntry);
        var parser = new StubParser(Clear);

        var e = await Assert.ThrowsAsync<QuickEntryException>(() => ParseText(Service(t, parser, perUser: 3)));

        Assert.Equal(QuickEntryErrorKind.UserDailyLimit, e.Kind);
        Assert.Contains("(3/3)", e.Message);
        Assert.Equal(0, parser.Calls);
    }

    [Fact]
    public async Task ParseAsync_ReceiptsDoNotUseTheQuickEntryAllowance()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 3, ScanKind.Receipt);
        AddScans(t, Bob, 3, ScanKind.QuickEntry);

        var service = Service(t, new StubParser(Clear), perUser: 3);

        await ParseText(service); // three receipts and Bob's entries leave Alice's allowance untouched

        Assert.Equal(1, (await service.GetQuotaAsync(Alice, Now)).UsedToday);
    }

    [Fact]
    public async Task ParseAsync_TheAppLimitIsSharedWithReceipts()
    {
        using var t = new TestDb();
        AddScans(t, Bob, 2, ScanKind.Receipt);
        AddScans(t, Bob, 1, ScanKind.QuickEntry);
        var parser = new StubParser(Clear);
        var service = Service(t, parser, total: 3);

        Assert.Equal(QuickEntryErrorKind.TotalDailyLimit, await FailureOf(() => ParseText(service)));
        Assert.True((await service.GetQuotaAsync(Alice, Now)).TotalLimitReached);
        Assert.Equal(0, parser.Calls);
    }

    [Fact]
    public async Task ParseAsync_TheDemoHasNoLimitAndIsNotCounted()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 5, ScanKind.QuickEntry, provider: "demo");
        var service = Service(t, new StubParser(Clear, isDemo: true), perUser: 1, total: 1);

        var proposal = await ParseText(service);
        var quota = await service.GetQuotaAsync(Alice, Now);

        Assert.True(proposal.IsDemo);
        Assert.Equal((0, true, false), (quota.UsedToday, quota.CanUse, quota.TotalLimitReached));
    }

    [Fact]
    public async Task GetQuotaAsync_CountsOnlyTodaysAnsweredQuickEntriesOfTheUser()
    {
        using var t = new TestDb();
        AddScans(t, Alice, 2, ScanKind.QuickEntry);
        AddScans(t, Alice, 1, ScanKind.QuickEntry, outcome: ReceiptScanOutcome.Unanswered);
        t.Context.ReceiptScans.Add(new ReceiptScan { UserId = Alice, CreatedAt = Today.AddMinutes(-1), Provider = "gemini", Kind = ScanKind.QuickEntry });
        t.Context.SaveChanges();

        var quota = await Service(t, new StubParser(Clear), perUser: 30).GetQuotaAsync(Alice, Now);

        Assert.Equal((2, 28, true), (quota.UsedToday, quota.Remaining, quota.CanUse));
    }

    [Fact]
    public async Task GetQuotaAsync_WithoutAReaderIsSwitchedOff()
    {
        using var t = new TestDb();
        Assert.False((await Service(t, null).GetQuotaAsync(Alice, Now)).CanUse);
    }

    // ---------- saving, undo, edit ----------

    private static readonly QuickEntrySaveLine[] TwoLines =
    [
        new("Simit", 20m, TransactionType.Expense, EatingOut, Today),
        new("Harçlık", 500m, TransactionType.Income, Income, Today.AddDays(-1))
    ];

    [Fact]
    public async Task SaveAsync_SavesEveryLineMarkedAsQuickEntry()
    {
        using var t = new TestDb();

        var saved = await Service(t, new StubParser(Clear)).SaveAsync(Alice, TwoLines);

        Assert.Equal(2, saved.Count);
        Assert.All(saved, s => Assert.Equal((Alice, "Hızlı giriş"), (s.UserId, s.Note)));
        Assert.Equal([("Simit", TransactionType.Expense, Today), ("Harçlık", TransactionType.Income, Today.AddDays(-1))],
            saved.Select(s => (s.ItemName!, s.Type, s.Date)));
    }

    [Fact]
    public async Task SaveAsync_SavesNothingWhenOneLineIsInvalid()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(Clear));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, [TwoLines[0], TwoLines[1] with { Date = Today.AddDays(1) }]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, [TwoLines[0], TwoLines[1] with { CategoryId = Market }]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, [TwoLines[0] with { Description = " " }]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Alice, []));

        Assert.Empty(t.Context.Transactions);
    }

    [Fact]
    public async Task UndoAsync_DeletesOnlyTheUsersQuickEntries()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(Clear));
        var saved = await service.SaveAsync(Alice, TwoLines);
        var typedByHand = AddExpense(t.Context, Alice, 99m, Today);
        var bobs = await service.SaveAsync(Bob, [TwoLines[0]]);

        var undone = await service.UndoAsync(Alice, [.. saved.Select(s => s.Id), typedByHand.Id, bobs[0].Id]);

        Assert.Equal(2, undone);
        Assert.Equal([typedByHand.Id, bobs[0].Id], t.Context.Transactions.Select(x => x.Id).OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task UndoAsync_TwiceDoesNothingTheSecondTime()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(Clear));
        var ids = (await service.SaveAsync(Alice, TwoLines)).Select(s => s.Id).ToList();

        Assert.Equal(2, await service.UndoAsync(Alice, ids));
        Assert.Equal(0, await service.UndoAsync(Alice, ids));
    }

    [Fact]
    public async Task GetSavedAsync_ReturnsOnlyTheUsersQuickEntries()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(Clear));
        var saved = await service.SaveAsync(Alice, TwoLines);
        var typedByHand = AddExpense(t.Context, Alice, 99m, Today);

        var found = await service.GetSavedAsync(Alice, [.. saved.Select(s => s.Id), typedByHand.Id]);
        var forBob = await service.GetSavedAsync(Bob, saved.Select(s => s.Id).ToList());

        Assert.Equal(saved.Select(s => s.Id), found.Select(f => f.Id));
        Assert.Empty(forBob);
    }

    [Fact]
    public async Task ReplaceAsync_SwapsTheBatchForTheEditedLines()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(Clear));
        var saved = await service.SaveAsync(Alice, TwoLines);
        var typedByHand = AddExpense(t.Context, Alice, 99m, Today);

        var replaced = await service.ReplaceAsync(Alice, [.. saved.Select(s => s.Id), typedByHand.Id],
            [new QuickEntrySaveLine("Simit ve çay", 35m, TransactionType.Expense, EatingOut, Today)]);

        var left = t.Context.Transactions.OrderBy(x => x.Id).ToList();
        Assert.Equal([typedByHand.Id, replaced[0].Id], left.Select(x => x.Id));
        Assert.Equal(("Simit ve çay", "Hızlı giriş"), (replaced[0].ItemName, replaced[0].Note));
    }

    [Fact]
    public async Task ReplaceAsync_KeepsTheBatchWhenAnEditedLineIsInvalid()
    {
        using var t = new TestDb();
        var service = Service(t, new StubParser(Clear));
        var saved = await service.SaveAsync(Alice, TwoLines);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplaceAsync(Alice, saved.Select(s => s.Id).ToList(),
            [new QuickEntrySaveLine("Simit", 0m, TransactionType.Expense, EatingOut, Today)]));

        Assert.Equal(2, t.Context.Transactions.Count());
    }

    [Theory]
    [InlineData(QuickEntryErrorKind.Timeout, true)]
    [InlineData(QuickEntryErrorKind.ServiceUnavailable, true)]
    [InlineData(QuickEntryErrorKind.Overloaded, true)]
    [InlineData(QuickEntryErrorKind.RateLimited, true)]
    [InlineData(QuickEntryErrorKind.QuotaExceeded, true)]
    [InlineData(QuickEntryErrorKind.Misconfigured, true)]
    [InlineData(QuickEntryErrorKind.NotUnderstood, false)]
    [InlineData(QuickEntryErrorKind.InvalidResponse, false)]
    [InlineData(QuickEntryErrorKind.Blocked, false)]
    public void IsUnanswered_IsTrueOnlyWhenTheReaderGaveNoAnswer(QuickEntryErrorKind kind, bool expected) =>
        Assert.Equal(expected, QuickEntryService.IsUnanswered(kind));
}
