using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Tests;

public class RecurringTransactionServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Today = new(2026, 10, 11);

    private static DateTime Oct(int day) => new(2026, 10, day);
    private static DateTime Nov(int day) => new(2026, 11, day);

    private static RecurringTransactionService NewService(TestDb t) => new(t.Context);

    private static RecurringTransaction Rent(int day, DateTime start, decimal amount = 3000m, DateTime? end = null) => new()
    {
        Type = TransactionType.Expense,
        Amount = amount,
        CategoryId = TestData.Rent,
        ItemName = "Yurt ücreti",
        DayOfMonth = day,
        StartDate = start,
        EndDate = end
    };

    private static RecurringTransaction Scholarship(int day, DateTime start, decimal amount = 2000m) => new()
    {
        Type = TransactionType.Income,
        Amount = amount,
        CategoryId = TestData.Income,
        DayOfMonth = day,
        StartDate = start
    };

    /// <summary>Creates a definition "as of" its start date, so past start dates are possible in tests.</summary>
    private static Task<RecurringTransaction> Create(TestDb t, RecurringTransaction input, string user = Alice) =>
        NewService(t).CreateAsync(user, input, input.StartDate);

    private static List<Transaction> Generated(TestDb t, string user = Alice) =>
        t.Context.Transactions.AsNoTracking().Where(x => x.UserId == user).OrderBy(x => x.Date).ToList();

    private static RecurringTransaction Reload(TestDb t, int id) =>
        t.Context.RecurringTransactions.AsNoTracking().Single(r => r.Id == id);

    // ---------- GenerateDueAsync: when ----------

    [Fact]
    public async Task GenerateDueAsync_CreatesTheTransactionOnItsDueDay()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 10, start: Oct(1)));

        var created = await NewService(t).GenerateDueAsync(Alice, Oct(10));

        var tx = Assert.Single(created);
        Assert.Equal(Oct(10), tx.Date);
        Assert.Equal(3000m, tx.Amount);
        Assert.Equal(TransactionType.Expense, tx.Type);
        Assert.Equal(TestData.Rent, tx.CategoryId);
        Assert.Equal("Yurt ücreti", tx.ItemName);
        Assert.Equal(Alice, tx.UserId);
        Assert.Equal(def.Id, tx.RecurringTransactionId);
        Assert.Equal(Oct(1), tx.RecurringPeriod);
        Assert.Equal(Oct(1), Reload(t, def.Id).LastGeneratedPeriod);
        Assert.Single(Generated(t));
    }

    [Fact]
    public async Task GenerateDueAsync_DoesNothingBeforeTheDueDay()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 10, start: Oct(1)));

        var created = await NewService(t).GenerateDueAsync(Alice, Oct(9));

        Assert.Empty(created);
        Assert.Empty(Generated(t));
        Assert.Null(Reload(t, def.Id).LastGeneratedPeriod);
    }

    [Fact]
    public async Task GenerateDueAsync_ADefinitionDueTodayIsGeneratedImmediately()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 11, start: Today));

        var created = await NewService(t).GenerateDueAsync(Alice, Today);

        Assert.Equal(Today, Assert.Single(created).Date);
    }

    [Fact]
    public async Task GenerateDueAsync_ReturnsTheCreatedTransactionsWithTheirCategory()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 10, start: Oct(1)));

        var created = await NewService(t).GenerateDueAsync(Alice, Oct(10));

        Assert.Equal("Kira/Yurt", Assert.Single(created).Category?.Name);
    }

    // ---------- GenerateDueAsync: never twice ----------

    [Fact]
    public async Task GenerateDueAsync_IsIdempotentWithinTheMonth()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 10, start: Oct(1)));
        var service = NewService(t);

        await service.GenerateDueAsync(Alice, Oct(10));
        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(10)));
        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(25)));

        Assert.Single(Generated(t));
    }

    [Fact]
    public async Task GenerateDueAsync_DoesNotRecreateAnOccurrenceTheUserDeleted()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 10, start: Oct(1)));
        var service = NewService(t);
        var created = await service.GenerateDueAsync(Alice, Oct(10));
        await new TransactionService(t.Context).DeleteAsync(Alice, created[0].Id);

        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(11)));
        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(31)));
        Assert.Empty(Generated(t));

        // The definition itself keeps running: next month comes as usual.
        Assert.Equal(Nov(10), Assert.Single(await service.GenerateDueAsync(Alice, Nov(10))).Date);
    }

    [Fact]
    public async Task GenerateDueAsync_SurvivesAUniqueIndexConflictWithoutDuplicating()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 10, start: Oct(1)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Oct(10));

        // Simulate a second request that still sees the old "last generated" value.
        var tracked = t.Context.RecurringTransactions.Single(r => r.Id == def.Id);
        tracked.LastGeneratedPeriod = null;
        t.Context.SaveChanges();

        var created = await service.GenerateDueAsync(Alice, Oct(10));

        Assert.Empty(created);
        Assert.Single(Generated(t));
    }

    [Fact]
    public void Database_RejectsTwoGeneratedTransactionsForTheSameDefinitionAndMonth()
    {
        using var t = new TestDb();
        var def = new RecurringTransaction
        {
            UserId = Alice, Type = TransactionType.Expense, Amount = 10m, CategoryId = TestData.Rent,
            DayOfMonth = 1, StartDate = Oct(1), CreatedAt = Oct(1)
        };
        t.Context.RecurringTransactions.Add(def);
        t.Context.SaveChanges();

        Transaction Occurrence() => new()
        {
            UserId = Alice, Type = TransactionType.Expense, Amount = 10m, Date = Oct(1), CategoryId = TestData.Rent,
            RecurringTransactionId = def.Id, RecurringPeriod = Oct(1)
        };
        t.Context.Transactions.Add(Occurrence());
        t.Context.SaveChanges();

        t.Context.Transactions.Add(Occurrence());
        Assert.Throws<DbUpdateException>(() => t.Context.SaveChanges());
    }

    // ---------- GenerateDueAsync: days that do not exist in every month ----------

    [Fact]
    public async Task GenerateDueAsync_Day31FallsOnTheLastDayOfShorterMonths()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 31, start: new DateTime(2026, 1, 1)));
        var service = NewService(t);

        await service.GenerateDueAsync(Alice, new DateTime(2026, 1, 31));
        Assert.Empty(await service.GenerateDueAsync(Alice, new DateTime(2026, 2, 27))); // not yet
        await service.GenerateDueAsync(Alice, new DateTime(2026, 2, 28));
        await service.GenerateDueAsync(Alice, new DateTime(2026, 4, 30));               // March and April together

        Assert.Equal(
            [new DateTime(2026, 1, 31), new DateTime(2026, 2, 28), new DateTime(2026, 3, 31), new DateTime(2026, 4, 30)],
            Generated(t).Select(x => x.Date).ToArray());
    }

    [Fact]
    public async Task GenerateDueAsync_UsesFebruary29InALeapYear()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 30, start: new DateTime(2028, 2, 1)));

        var created = await NewService(t).GenerateDueAsync(Alice, new DateTime(2028, 2, 29));

        Assert.Equal(new DateTime(2028, 2, 29), Assert.Single(created).Date);
    }

    // ---------- GenerateDueAsync: catching up ----------

    [Fact]
    public async Task GenerateDueAsync_CatchesUpTheMonthsTheUserWasAway()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 5, start: new DateTime(2026, 7, 1)));

        var created = await NewService(t).GenerateDueAsync(Alice, Today);

        Assert.Equal(
            [new DateTime(2026, 7, 5), new DateTime(2026, 8, 5), new DateTime(2026, 9, 5), Oct(5)],
            created.Select(x => x.Date).ToArray());
        Assert.Equal(
            [new DateTime(2026, 7, 1), new DateTime(2026, 8, 1), new DateTime(2026, 9, 1), Oct(1)],
            created.Select(x => x.RecurringPeriod!.Value).ToArray());
        Assert.All(created, x => Assert.True(x.Date <= Today)); // never future-dated
    }

    [Fact]
    public async Task GenerateDueAsync_CatchUpGoesBackAtMostTwelveMonths()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: new DateTime(2025, 1, 1)));

        var created = await NewService(t).GenerateDueAsync(Alice, Today);

        Assert.Equal(12, created.Count);
        Assert.Equal(new DateTime(2025, 11, 5), created.Min(x => x.Date));
        Assert.Equal(Oct(5), created.Max(x => x.Date));
        Assert.Equal(Oct(1), Reload(t, def.Id).LastGeneratedPeriod);
    }

    [Fact]
    public async Task GenerateDueAsync_MissedExpensesAreInPlaceBeforeTheMonthIsClosed()
    {
        using var t = new TestDb();
        TestData.AddIncome(t.Context, Alice, 2000m, new DateTime(2026, 9, 1));
        await Create(t, Rent(day: 28, start: new DateTime(2026, 9, 1), amount: 500m));

        // The user did not open the app after 28 September; on 2 October generation runs first.
        await NewService(t).GenerateDueAsync(Alice, Oct(2));
        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Oct(2));

        Assert.Equal(1500m, pending!.Remaining); // 2000 - 500, not 2000
    }

    // ---------- GenerateDueAsync: start, end, pause ----------

    [Fact]
    public async Task GenerateDueAsync_NeverGeneratesAnOccurrenceDueBeforeTheStartDate()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Today)); // this month's 5th has already passed
        var service = NewService(t);

        Assert.Empty(await service.GenerateDueAsync(Alice, Today));
        Assert.Equal(Oct(1), Reload(t, def.Id).LastGeneratedPeriod); // October is settled: nothing to generate
        Assert.Empty(await service.GenerateDueAsync(Alice, Nov(4)));

        Assert.Equal(Nov(5), Assert.Single(await service.GenerateDueAsync(Alice, Nov(5))).Date);
        Assert.All(Generated(t), x => Assert.True(x.Date >= Oct(1))); // nothing lands in an earlier month
    }

    [Fact]
    public async Task GenerateDueAsync_WaitsForAStartDateInTheFuture()
    {
        using var t = new TestDb();
        await NewService(t).CreateAsync(Alice, Rent(day: 3, start: Nov(1)), Today);
        var service = NewService(t);

        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(31)));
        Assert.Equal(Nov(3), Assert.Single(await service.GenerateDueAsync(Alice, Nov(3))).Date);
    }

    [Theory]
    [InlineData(10, 2)] // end date on the due day: still generated
    [InlineData(9, 1)]  // end date one day earlier: November is not generated
    [InlineData(30, 2)]
    public async Task GenerateDueAsync_StopsAfterTheEndDate(int endDayInNovember, int expected)
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 10, start: Oct(1), end: Nov(endDayInNovember)));

        var created = await NewService(t).GenerateDueAsync(Alice, new DateTime(2026, 12, 20));

        Assert.Equal(expected, created.Count);
        Assert.DoesNotContain(created, x => x.Date.Month == 12);
    }

    [Fact]
    public async Task GenerateDueAsync_SkipsPausedDefinitionsAndResumeDoesNotBackfill()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Oct(5));
        await service.PauseAsync(Alice, def.Id);

        Assert.Empty(await service.GenerateDueAsync(Alice, new DateTime(2027, 1, 10)));

        await service.ResumeAsync(Alice, def.Id, new DateTime(2027, 1, 10));
        Assert.Empty(await service.GenerateDueAsync(Alice, new DateTime(2027, 1, 10))); // 5 January already passed
        await service.GenerateDueAsync(Alice, new DateTime(2027, 2, 5));

        Assert.Equal([Oct(5), new DateTime(2027, 2, 5)], Generated(t).Select(x => x.Date).ToArray());
    }

    [Fact]
    public async Task GenerateDueAsync_ResumingBeforeTheDueDayStillGeneratesThisMonth()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 12, start: Oct(1)));
        var service = NewService(t);
        await service.PauseAsync(Alice, def.Id);
        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(4)));

        await service.ResumeAsync(Alice, def.Id, Oct(5));

        Assert.Equal(Oct(12), Assert.Single(await service.GenerateDueAsync(Alice, Oct(12))).Date);
    }

    [Fact]
    public async Task GenerateDueAsync_OnlyTouchesTheGivenUser()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 10, start: Oct(1)));
        var bobs = await Create(t, Rent(day: 10, start: Oct(1)), Bob);

        await NewService(t).GenerateDueAsync(Alice, Oct(10));

        Assert.Single(Generated(t));
        Assert.Empty(Generated(t, Bob));
        Assert.Null(Reload(t, bobs.Id).LastGeneratedPeriod);
    }

    // ---------- GenerateDueAsync: after editing the definition ----------

    [Fact]
    public async Task GenerateDueAsync_ChangingTheDayNeverCreatesASecondOccurrenceInTheSameMonth()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Oct(5));

        var edit = Rent(day: 25, start: Oct(1));
        edit.Id = def.Id;
        await service.UpdateAsync(Alice, edit, Today);

        Assert.Empty(await service.GenerateDueAsync(Alice, Oct(25)));
        Assert.Empty(await service.GenerateDueAsync(Alice, Nov(24)));
        await service.GenerateDueAsync(Alice, Nov(25));

        Assert.Equal([Oct(5), Nov(25)], Generated(t).Select(x => x.Date).ToArray());
    }

    [Fact]
    public async Task GenerateDueAsync_MovingTheDayIntoThePastGeneratesThisMonthsOccurrenceOnTheNewDay()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 25, start: Oct(1)));
        var service = NewService(t);
        Assert.Empty(await service.GenerateDueAsync(Alice, Today));

        var edit = Rent(day: 5, start: Oct(1));
        edit.Id = def.Id;
        await service.UpdateAsync(Alice, edit, Today);

        Assert.Equal(Oct(5), Assert.Single(await service.GenerateDueAsync(Alice, Today)).Date);
    }

    [Fact]
    public async Task GenerateDueAsync_AnEditedAmountAppliesToFutureOccurrencesOnly()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Oct(5));

        var edit = Rent(day: 5, start: Oct(1), amount: 3500m);
        edit.Id = def.Id;
        await service.UpdateAsync(Alice, edit, Today);
        await service.GenerateDueAsync(Alice, Nov(5));

        Assert.Equal([3000m, 3500m], Generated(t).Select(x => x.Amount).ToArray());
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_PersistsACleanedDefinition()
    {
        using var t = new TestDb();
        var input = new RecurringTransaction
        {
            Type = TransactionType.Expense, Amount = 189.994m, CategoryId = TestData.Bills,
            ItemName = "  Netflix ", Note = "   ", DayOfMonth = 12,
            StartDate = new DateTime(2026, 10, 11, 15, 30, 0), EndDate = new DateTime(2027, 6, 30)
        };

        var created = await NewService(t).CreateAsync(Alice, input, Today);

        var saved = Reload(t, created.Id);
        Assert.Equal(Alice, saved.UserId);
        Assert.Equal(189.99m, saved.Amount);
        Assert.Equal("Netflix", saved.ItemName);
        Assert.Null(saved.Note);
        Assert.Equal(12, saved.DayOfMonth);
        Assert.Equal(Today, saved.StartDate); // date only
        Assert.Equal(new DateTime(2027, 6, 30), saved.EndDate);
        Assert.True(saved.IsActive);
        Assert.Null(saved.LastGeneratedPeriod);
    }

    [Theory]
    [InlineData("amount-zero")]
    [InlineData("amount-negative")]
    [InlineData("day-zero")]
    [InlineData("day-32")]
    [InlineData("unknown-category")]
    [InlineData("category-of-other-type")]
    [InlineData("start-before-this-month")]
    [InlineData("end-before-start")]
    public async Task CreateAsync_RejectsInvalidDefinitions(string problem)
    {
        using var t = new TestDb();
        var input = Rent(day: 5, start: Today);
        switch (problem)
        {
            case "amount-zero": input.Amount = 0m; break;
            case "amount-negative": input.Amount = -5m; break;
            case "day-zero": input.DayOfMonth = 0; break;
            case "day-32": input.DayOfMonth = 32; break;
            case "unknown-category": input.CategoryId = 999; break;
            case "category-of-other-type": input.CategoryId = TestData.Income; break;
            case "start-before-this-month": input.StartDate = new DateTime(2026, 9, 30); break;
            case "end-before-start": input.EndDate = Oct(10); break;
        }

        await Assert.ThrowsAsync<ArgumentException>(() => NewService(t).CreateAsync(Alice, input, Today));
        Assert.Empty(t.Context.RecurringTransactions);
    }

    [Fact]
    public async Task CreateAsync_AllowsTheFirstDayOfTheCurrentMonthAndFutureStartDates()
    {
        using var t = new TestDb();
        var service = NewService(t);

        await service.CreateAsync(Alice, Rent(day: 5, start: Oct(1)), Today);
        await service.CreateAsync(Alice, Rent(day: 5, start: new DateTime(2027, 1, 15)), Today);

        Assert.Equal(2, t.Context.RecurringTransactions.Count());
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_ChangesTheEditableFieldsOnly()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Oct(5));

        var edit = new RecurringTransaction
        {
            Id = def.Id, Type = TransactionType.Expense, Amount = 3250.5m, CategoryId = TestData.Bills,
            ItemName = "Yeni yurt", Note = "zamlı", DayOfMonth = 3, EndDate = new DateTime(2027, 6, 30),
            // these must be ignored:
            StartDate = new DateTime(2020, 1, 1), IsActive = false, LastGeneratedPeriod = null
        };

        Assert.True(await service.UpdateAsync(Alice, edit, Today));

        var saved = Reload(t, def.Id);
        Assert.Equal(3250.5m, saved.Amount);
        Assert.Equal(TestData.Bills, saved.CategoryId);
        Assert.Equal("Yeni yurt", saved.ItemName);
        Assert.Equal("zamlı", saved.Note);
        Assert.Equal(3, saved.DayOfMonth);
        Assert.Equal(new DateTime(2027, 6, 30), saved.EndDate);
        Assert.Equal(Oct(1), saved.StartDate);
        Assert.True(saved.IsActive);
        Assert.Equal(Oct(1), saved.LastGeneratedPeriod);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalseForAnotherUsersDefinition()
    {
        using var t = new TestDb();
        var bobs = await Create(t, Rent(day: 5, start: Oct(1)), Bob);
        var edit = Rent(day: 9, start: Oct(1), amount: 1m);
        edit.Id = bobs.Id;

        Assert.False(await NewService(t).UpdateAsync(Alice, edit, Today));
        Assert.Equal(3000m, Reload(t, bobs.Id).Amount);
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidValues()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var edit = Rent(day: 5, start: Oct(1), amount: 0m);
        edit.Id = def.Id;

        await Assert.ThrowsAsync<ArgumentException>(() => NewService(t).UpdateAsync(Alice, edit, Today));
        Assert.Equal(3000m, Reload(t, def.Id).Amount);
    }

    [Fact]
    public async Task UpdateAsync_ReopeningAnEndedDefinitionDoesNotBackfillTheGap()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: new DateTime(2026, 7, 1), end: new DateTime(2026, 8, 31)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Today); // July and August

        var edit = Rent(day: 5, start: new DateTime(2026, 7, 1)); // end date removed
        edit.Id = def.Id;
        await service.UpdateAsync(Alice, edit, Today);

        Assert.Equal(Today, Reload(t, def.Id).StartDate);
        Assert.Empty(await service.GenerateDueAsync(Alice, Today)); // no September, no October (the 5th has passed)
        await service.GenerateDueAsync(Alice, Nov(5));
        Assert.Equal(
            [new DateTime(2026, 7, 5), new DateTime(2026, 8, 5), Nov(5)],
            Generated(t).Select(x => x.Date).ToArray());
    }

    // ---------- PauseAsync / ResumeAsync / DeleteAsync ----------

    [Fact]
    public async Task PauseAsync_And_ResumeAsync_ToggleTheDefinition()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);

        Assert.True(await service.PauseAsync(Alice, def.Id));
        Assert.False(Reload(t, def.Id).IsActive);

        Assert.True(await service.ResumeAsync(Alice, def.Id, Today));
        var resumed = Reload(t, def.Id);
        Assert.True(resumed.IsActive);
        Assert.Equal(Today, resumed.StartDate);
    }

    [Fact]
    public async Task ResumeAsync_KeepsAStartDateThatIsStillInTheFuture()
    {
        using var t = new TestDb();
        var service = NewService(t);
        var def = await service.CreateAsync(Alice, Rent(day: 3, start: Nov(1)), Today);
        await service.PauseAsync(Alice, def.Id);

        await service.ResumeAsync(Alice, def.Id, Today);

        Assert.Equal(Nov(1), Reload(t, def.Id).StartDate);
    }

    [Fact]
    public async Task PauseAsync_And_ResumeAsync_ReturnFalseForAnotherUsersDefinition()
    {
        using var t = new TestDb();
        var bobs = await Create(t, Rent(day: 5, start: Oct(1)), Bob);
        var service = NewService(t);

        Assert.False(await service.PauseAsync(Alice, bobs.Id));
        Assert.False(await service.ResumeAsync(Alice, bobs.Id, Today));
        Assert.True(Reload(t, bobs.Id).IsActive);
        Assert.Equal(Oct(1), Reload(t, bobs.Id).StartDate);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheDefinitionButKeepsItsTransactionsAsOrdinaryOnes()
    {
        using var t = new TestDb();
        var def = await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        await service.GenerateDueAsync(Alice, Oct(5));

        Assert.True(await service.DeleteAsync(Alice, def.Id));

        Assert.Empty(t.Context.RecurringTransactions);
        var tx = Assert.Single(Generated(t));
        Assert.Null(tx.RecurringTransactionId);
        Assert.Equal(3000m, tx.Amount);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForAnotherUsersDefinition()
    {
        using var t = new TestDb();
        var bobs = await Create(t, Rent(day: 5, start: Oct(1)), Bob);

        Assert.False(await NewService(t).DeleteAsync(Alice, bobs.Id));
        Assert.Single(t.Context.RecurringTransactions);
    }

    // ---------- GetAllAsync / GetAsync ----------

    [Fact]
    public async Task GetAllAsync_ReturnsOwnDefinitionsWithNextDueDateAndStatus()
    {
        using var t = new TestDb();
        var service = NewService(t);
        var upcoming = await Create(t, Scholarship(day: 20, start: Oct(1)));
        var generated = await Create(t, Rent(day: 5, start: Oct(1)));
        var paused = await Create(t, Rent(day: 12, start: Oct(1), amount: 190m));
        var ended = await Create(t, Rent(day: 5, start: new DateTime(2026, 7, 1), amount: 50m, end: new DateTime(2026, 8, 31)));
        await Create(t, Rent(day: 1, start: Oct(1)), Bob);
        await service.PauseAsync(Alice, paused.Id);
        await service.GenerateDueAsync(Alice, Today);

        var all = await service.GetAllAsync(Alice, Today);

        Assert.Equal(4, all.Count);
        RecurringListItem Item(int id) => all.Single(i => i.Definition.Id == id);
        Assert.Equal((RecurringStatus.Active, (DateTime?)Oct(20)), (Item(upcoming.Id).Status, Item(upcoming.Id).NextDueDate));
        Assert.Equal((RecurringStatus.Active, (DateTime?)Nov(5)), (Item(generated.Id).Status, Item(generated.Id).NextDueDate));
        Assert.Equal((RecurringStatus.Paused, (DateTime?)null), (Item(paused.Id).Status, Item(paused.Id).NextDueDate));
        Assert.Equal((RecurringStatus.Ended, (DateTime?)null), (Item(ended.Id).Status, Item(ended.Id).NextDueDate));
        Assert.All(all, i => Assert.NotNull(i.Definition.Category));
        Assert.All(all, i => Assert.Equal(Alice, i.Definition.UserId));
    }

    [Fact]
    public async Task GetAllAsync_ShowsTheLastDayForDay31InAShortMonth()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 31, start: Nov(1)));

        var item = Assert.Single(await NewService(t).GetAllAsync(Alice, Nov(11)));

        Assert.Equal(Nov(30), item.NextDueDate);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForAnotherUsersDefinition()
    {
        using var t = new TestDb();
        var bobs = await Create(t, Rent(day: 5, start: Oct(1)), Bob);
        var service = NewService(t);

        Assert.Null(await service.GetAsync(Alice, bobs.Id));
        Assert.Equal("Kira/Yurt", (await service.GetAsync(Bob, bobs.Id))!.Category!.Name);
    }

    // ---------- GetPendingAsync ----------

    [Fact]
    public async Task GetPendingAsync_ListsThisMonthsOccurrencesThatHaveNotBeenGeneratedYet()
    {
        using var t = new TestDb();
        var service = NewService(t);
        await Create(t, Scholarship(day: 20, start: Oct(1)));                 // later this month
        await Create(t, Rent(day: 14, start: Oct(1), amount: 60m));           // later this month
        await Create(t, Rent(day: 5, start: Oct(1)));                         // generated below
        await service.GenerateDueAsync(Alice, Today);

        var pending = await service.GetPendingAsync(Alice, Today);

        Assert.Equal([Oct(14), Oct(20)], pending.Select(p => p.DueDate).ToArray()); // ordered by due date
        Assert.Equal([60m, 2000m], pending.Select(p => p.Definition.Amount).ToArray());
        Assert.All(pending, p => Assert.NotNull(p.Definition.Category));
    }

    [Fact]
    public async Task GetPendingAsync_StillCountsAnOccurrenceThatIsDueButNotGeneratedYet()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 5, start: Oct(1)));

        var pending = await NewService(t).GetPendingAsync(Alice, Today); // generation has not run

        Assert.Equal(Oct(5), Assert.Single(pending).DueDate);
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesPausedFutureEndedAndOtherUsersDefinitions()
    {
        using var t = new TestDb();
        var service = NewService(t);
        var paused = await Create(t, Rent(day: 20, start: Oct(1)));
        await service.PauseAsync(Alice, paused.Id);
        await service.CreateAsync(Alice, Rent(day: 20, start: Nov(1)), Today);                 // starts next month
        await Create(t, Rent(day: 20, start: Oct(1), end: Oct(15)));                           // ends before its due day
        await Create(t, Rent(day: 5, start: Today));                                           // this month's day already passed at creation
        await Create(t, Rent(day: 20, start: Oct(1)), Bob);

        Assert.Empty(await service.GetPendingAsync(Alice, Today));
    }

    [Fact]
    public async Task GetPendingAsync_IncludesAnOccurrenceDueExactlyOnTheEndDate()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 20, start: Oct(1), end: Oct(20)));

        Assert.Single(await NewService(t).GetPendingAsync(Alice, Today));
    }

    [Fact]
    public async Task GetPendingAsync_DoesNotListAnOccurrenceTheUserDeleted()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        var created = await service.GenerateDueAsync(Alice, Today);
        await new TransactionService(t.Context).DeleteAsync(Alice, created[0].Id);

        Assert.Empty(await service.GetPendingAsync(Alice, Today));
    }

    // ---------- GetSummaryAsync ----------

    [Fact]
    public async Task GetSummaryAsync_SumsOnlyDefinitionsThatAreStillRunning()
    {
        using var t = new TestDb();
        var service = NewService(t);
        await Create(t, Scholarship(day: 5, start: Oct(1), amount: 2000m));
        await Create(t, Scholarship(day: 1, start: Oct(1), amount: 3500m));
        await Create(t, Rent(day: 1, start: Oct(1), amount: 3000m));
        var paused = await Create(t, Rent(day: 12, start: Oct(1), amount: 190m));
        await service.PauseAsync(Alice, paused.Id);
        await Create(t, Rent(day: 5, start: new DateTime(2026, 7, 1), amount: 50m, end: new DateTime(2026, 8, 31)));
        await Create(t, Rent(day: 1, start: Oct(1), amount: 9999m), Bob);
        await service.GenerateDueAsync(Alice, Today);

        var summary = await service.GetSummaryAsync(Alice, Today);

        Assert.Equal(5500m, summary.MonthlyIncome);
        Assert.Equal(3000m, summary.MonthlyExpense);
        Assert.Equal(2500m, summary.Net);
    }

    [Fact]
    public async Task GetSummaryAsync_IsZeroWithoutDefinitions()
    {
        using var t = new TestDb();

        Assert.Equal(new RecurringSummary(0m, 0m), await NewService(t).GetSummaryAsync(Alice, Today));
    }

    // ---------- CreateFromTransactionAsync ("Her ay tekrarla") ----------

    [Fact]
    public async Task CreateFromTransactionAsync_TurnsATransactionIntoTheFirstOccurrenceOfANewDefinition()
    {
        using var t = new TestDb();
        var tx = TestData.AddExpense(t.Context, Alice, 3000m, Oct(5), TestData.Rent, "Yurt ücreti");
        var service = NewService(t);

        var def = await service.CreateFromTransactionAsync(Alice, tx.Id, Today);

        Assert.NotNull(def);
        var saved = Reload(t, def.Id);
        Assert.Equal((TransactionType.Expense, 3000m, TestData.Rent, "Yurt ücreti", 5),
            (saved.Type, saved.Amount, saved.CategoryId, saved.ItemName, saved.DayOfMonth));
        Assert.Equal(Oct(5), saved.StartDate);
        Assert.Equal(Oct(1), saved.LastGeneratedPeriod);
        Assert.True(saved.IsActive);

        var linked = Assert.Single(Generated(t));
        Assert.Equal(def.Id, linked.RecurringTransactionId);
        Assert.Equal(Oct(1), linked.RecurringPeriod);

        Assert.Empty(await service.GenerateDueAsync(Alice, Today));          // not a second time this month
        Assert.Equal(Nov(5), Assert.Single(await service.GenerateDueAsync(Alice, Nov(5))).Date);
    }

    [Fact]
    public async Task CreateFromTransactionAsync_ForAnOlderTransactionStartsFromTheCurrentMonth()
    {
        using var t = new TestDb();
        var tx = TestData.AddExpense(t.Context, Alice, 300m, new DateTime(2026, 8, 20), TestData.Bills, "İnternet");
        var service = NewService(t);

        var def = await service.CreateFromTransactionAsync(Alice, tx.Id, Today);

        Assert.Equal(Oct(1), Reload(t, def!.Id).StartDate);
        Assert.Empty(await service.GenerateDueAsync(Alice, Today));          // September is not backfilled, 20 October is not due yet
        Assert.Equal(Oct(20), Assert.Single(await service.GenerateDueAsync(Alice, Oct(20))).Date);
        Assert.DoesNotContain(Generated(t), x => x.Date.Month == 9);
    }

    [Fact]
    public async Task CreateFromTransactionAsync_ReturnsNullForUnknownOrForeignTransactions()
    {
        using var t = new TestDb();
        var bobs = TestData.AddExpense(t.Context, Bob, 10m, Oct(5));
        var service = NewService(t);

        Assert.Null(await service.CreateFromTransactionAsync(Alice, bobs.Id, Today));
        Assert.Null(await service.CreateFromTransactionAsync(Alice, 999, Today));
        Assert.Empty(t.Context.RecurringTransactions);
    }

    [Fact]
    public async Task CreateFromTransactionAsync_RejectsATransactionThatAlreadyBelongsToADefinition()
    {
        using var t = new TestDb();
        await Create(t, Rent(day: 5, start: Oct(1)));
        var service = NewService(t);
        var generated = await service.GenerateDueAsync(Alice, Oct(5));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateFromTransactionAsync(Alice, generated[0].Id, Today));
        Assert.Single(t.Context.RecurringTransactions);
    }
}
