using BudgetApp.Models;
using BudgetApp.Services;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class DailyAllowanceServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const int FamilySupport = 2; // "Aile Desteği"
    private static readonly DateTime Today = new(2026, 10, 12);

    private static DateTime Oct(int day) => new(2026, 10, day);
    private static DateTime Sep(int day) => new(2026, 9, day);

    private static Task<DailyAllowance?> Get(TestDb t, DateTime? today = null, string user = Alice) =>
        new DailyAllowanceService(t.Context).GetAsync(user, today ?? Today);

    /// <summary>A monthly income whose occurrence of this month is still to come (September was the last one handled).</summary>
    private static RecurringTransaction IncomeOn(TestDb t, int day, decimal amount = 2000m, string user = Alice, int? category = null) =>
        AddRecurring(t.Context, user, TransactionType.Income, amount, day, Sep(1), categoryId: category, lastGenerated: Sep(1));

    /// <summary>A monthly expense; <paramref name="handledThisMonth"/> means October's occurrence already exists.</summary>
    private static RecurringTransaction ExpenseOn(TestDb t, int day, decimal amount, bool handledThisMonth = false, int category = Bills) =>
        AddRecurring(t.Context, Alice, TransactionType.Expense, amount, day, Sep(1), categoryId: category,
            lastGenerated: handledThisMonth ? Oct(1) : Sep(1));

    /// <summary>1.000 in, 200 out, scholarship on the 20th: 800 on hand for 8 days (12th..19th) = 100 a day.</summary>
    private static void SeedBase(TestDb t)
    {
        AddIncome(t.Context, Alice, 1000m, Oct(1));
        AddExpense(t.Context, Alice, 200m, Oct(5), Market);
        IncomeOn(t, day: 20);
    }

    // ---------- the basic division ----------

    [Fact]
    public async Task GetAsync_DividesWhatIsAvailableByTheDaysUntilTheNextIncome()
    {
        using var t = new TestDb();
        SeedBase(t);

        var allowance = await Get(t);

        Assert.NotNull(allowance);
        Assert.Equal(Oct(20), allowance.NextIncomeDate);
        Assert.Equal(Oct(20), allowance.PeriodEnd);
        Assert.Equal("KYK Bursu", allowance.NextIncomeName);
        Assert.True(allowance.HasRecurringIncome);
        Assert.Equal(8, allowance.DaysLeft);              // today included, the income day not
        Assert.Equal(800m, allowance.Available);
        Assert.Equal(0m, allowance.UpcomingExpenses);
        Assert.Equal(100m, allowance.DailyAmount);
        Assert.Equal(0m, allowance.SpentToday);
        Assert.Equal(100m, allowance.RemainingToday);
        Assert.Equal(AllowanceStatus.Ok, allowance.Status);
    }

    [Fact]
    public async Task GetAsync_RoundsTheDailyAmountDownToTheKurus()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 100m, Oct(1));
        IncomeOn(t, day: 15); // 3 days

        var allowance = await Get(t);

        Assert.Equal(33.33m, allowance!.DailyAmount);
        Assert.Equal(50m, allowance.TomorrowAmount);
    }

    [Fact]
    public async Task GetAsync_UsesTheItemNameOfTheIncomeWhenItHasOne()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct(1));
        AddRecurring(t.Context, Alice, TransactionType.Income, 1500m, 20, Sep(1), itemName: "Kafe maaşı", lastGenerated: Sep(1));

        Assert.Equal("Kafe maaşı", (await Get(t))!.NextIncomeName);
    }

    // ---------- measured at the start of the day ----------

    [Fact]
    public async Task GetAsync_TodaysSpendingUsesUpTheAllowanceWithoutShrinkingIt()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 30m, Today, Coffee);

        var allowance = await Get(t);

        Assert.Equal(100m, allowance!.DailyAmount);    // unchanged
        Assert.Equal(800m, allowance.Available);       // measured before today's spending
        Assert.Equal(30m, allowance.SpentToday);
        Assert.Equal(70m, allowance.RemainingToday);
        Assert.Equal(AllowanceStatus.Ok, allowance.Status);
    }

    [Fact]
    public async Task GetAsync_SpendingLessThanTheAllowanceRaisesTomorrows()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 30m, Today, Coffee);

        var today = await Get(t);
        var tomorrow = await Get(t, Oct(13));

        Assert.Equal(110m, today!.TomorrowAmount);     // (800 - 30) / 7
        Assert.Equal(110m, tomorrow!.DailyAmount);     // and that is exactly what the next day shows
        Assert.Equal(7, tomorrow.DaysLeft);
        Assert.Equal(0m, tomorrow.SpentToday);
    }

    [Fact]
    public async Task GetAsync_SpendingMoreThanTheAllowanceIsFlaggedAndLowersTomorrows()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 150m, Today, EatingOut);

        var today = await Get(t);
        var tomorrow = await Get(t, Oct(13));

        Assert.Equal(100m, today!.DailyAmount);
        Assert.Equal(-50m, today.RemainingToday);
        Assert.Equal(AllowanceStatus.OverToday, today.Status);
        Assert.Equal(92.85m, today.TomorrowAmount);    // 650 / 7, rounded down
        Assert.Equal(92.85m, tomorrow!.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_TodaysFixedExpensesComeOffThePoolNotOffTodaysAllowance()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 240m, Today, Rent);                       // fixed category
        AddGeneratedExpense(t.Context, Alice, 160m, Today, Transport);          // recurring occurrence in a variable category

        var allowance = await Get(t);

        Assert.Equal(400m, allowance!.Available);      // 800 - 240 - 160
        Assert.Equal(50m, allowance.DailyAmount);
        Assert.Equal(0m, allowance.SpentToday);
        Assert.Equal(50m, allowance.RemainingToday);
    }

    [Fact]
    public async Task GetAsync_IncomeThatArrivedTodayCountsFromThisMorning()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddIncome(t.Context, Alice, 500m, Today);

        var allowance = await Get(t);

        Assert.Equal(1300m, allowance!.Available);
        Assert.Equal(162.5m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_WhenTheRecurringIncomeCameTodayTheNextOneIsNextMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 2000m, Today);
        // Day 12, and October's occurrence has been generated (that is the income above).
        AddRecurring(t.Context, Alice, TransactionType.Income, 2000m, 12, Sep(1), lastGenerated: Oct(1));

        var allowance = await Get(t);

        Assert.Equal(new DateTime(2026, 11, 12), allowance!.NextIncomeDate);
        Assert.Equal(31, allowance.DaysLeft);
        Assert.Equal(64.51m, allowance.DailyAmount);   // 2000 / 31
    }

    [Fact]
    public async Task GetAsync_WhenTheIncomeArrivesTomorrowEverythingIsTodaysAllowance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct(1));
        AddExpense(t.Context, Alice, 200m, Oct(5), Market);
        IncomeOn(t, day: 13);

        var allowance = await Get(t);

        Assert.Equal(1, allowance!.DaysLeft);
        Assert.Equal(800m, allowance.DailyAmount);
        Assert.Null(allowance.TomorrowAmount);
    }

    // ---------- which income is "next" ----------

    [Fact]
    public async Task GetAsync_PicksTheNearestOfSeveralRecurringIncomes()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 900m, Oct(1));
        IncomeOn(t, day: 20);
        IncomeOn(t, day: 15, amount: 3500m, category: FamilySupport);

        var allowance = await Get(t);

        Assert.Equal(Oct(15), allowance!.NextIncomeDate);
        Assert.Equal("Aile Desteği", allowance.NextIncomeName);
        Assert.Equal(3, allowance.DaysLeft);
        Assert.Equal(300m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_IgnoresPausedEndedAndAlreadyHandledIncomeDefinitions()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 800m, Oct(1));
        AddRecurring(t.Context, Alice, TransactionType.Income, 1m, 13, Sep(1), isActive: false, lastGenerated: Sep(1));       // paused
        AddRecurring(t.Context, Alice, TransactionType.Income, 1m, 14, Sep(1), lastGenerated: Sep(1), end: Oct(10));          // ended
        AddRecurring(t.Context, Alice, TransactionType.Income, 1m, 5, Sep(1), lastGenerated: Oct(1));                         // came on the 5th
        IncomeOn(t, day: 20);

        var allowance = await Get(t);

        Assert.Equal(Oct(20), allowance!.NextIncomeDate);
    }

    [Fact]
    public async Task GetAsync_AnIncomeThatStartsLaterCountsFromItsFirstOccurrence()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 660m, Oct(1));
        AddRecurring(t.Context, Alice, TransactionType.Income, 1500m, 14, new DateTime(2026, 11, 1));

        var allowance = await Get(t);

        Assert.Equal(new DateTime(2026, 11, 14), allowance!.NextIncomeDate);
        Assert.Equal(33, allowance.DaysLeft);
        Assert.Equal(20m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_Day31FallsOnTheLastDayOfAShortMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 11, 1));
        AddRecurring(t.Context, Alice, TransactionType.Income, 2000m, 31, Sep(1), lastGenerated: Oct(1));

        var allowance = await Get(t, new DateTime(2026, 11, 12));

        Assert.Equal(new DateTime(2026, 11, 30), allowance!.NextIncomeDate);
        Assert.Equal(18, allowance.DaysLeft);
        Assert.Equal(55.55m, allowance.DailyAmount);
    }

    // ---------- no recurring income ----------

    [Fact]
    public async Task GetAsync_WithoutRecurringIncomeTheMoneyHasToLastUntilTheEndOfTheMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct(1));

        var allowance = await Get(t);

        Assert.False(allowance!.HasRecurringIncome);
        Assert.Null(allowance.NextIncomeDate);
        Assert.Null(allowance.NextIncomeName);
        Assert.Equal(new DateTime(2026, 11, 1), allowance.PeriodEnd);
        Assert.Equal(20, allowance.DaysLeft);          // 12th..31st, both included
        Assert.Equal(50m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_WithoutRecurringIncomeOnlyThisMonthsUpcomingExpensesAreReserved()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct(1));
        ExpenseOn(t, day: 25, amount: 300m);                            // 25 October: reserved
        ExpenseOn(t, day: 3, amount: 400m, handledThisMonth: true);     // next one is 3 November: not this period

        var allowance = await Get(t);

        Assert.Equal(300m, allowance!.UpcomingExpenses);
        Assert.Equal(700m, allowance.Available);
        Assert.Equal(35m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_OnTheLastDayOfTheMonthWithoutRecurringIncomeOneDayIsLeft()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct(1));
        AddExpense(t.Context, Alice, 880m, Oct(20), Market);

        var allowance = await Get(t, Oct(31));

        Assert.Equal(1, allowance!.DaysLeft);
        Assert.Equal(120m, allowance.DailyAmount);
        Assert.Null(allowance.TomorrowAmount);
    }

    // ---------- the next income is in the next month ----------

    [Fact]
    public async Task GetAsync_OnTheLastDayOfTheMonthTheNextIncomeIsNextMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct(5));
        AddRecurring(t.Context, Alice, TransactionType.Income, 2000m, 5, Sep(1), lastGenerated: Oct(1));

        var allowance = await Get(t, Oct(31));

        Assert.Equal(new DateTime(2026, 11, 5), allowance!.NextIncomeDate);
        Assert.Equal(5, allowance.DaysLeft);           // 31 Oct, 1-4 Nov
        Assert.Equal(200m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_ReservesRecurringExpensesUpToTheNextIncomeAcrossTheMonthBoundary()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct(1));
        AddRecurring(t.Context, Alice, TransactionType.Income, 2000m, 5, Sep(1), lastGenerated: Oct(1));   // next: 5 November
        ExpenseOn(t, day: 1, amount: 3000m, handledThisMonth: true, category: Rent);                       // 1 November: before the income
        ExpenseOn(t, day: 30, amount: 190m);                                                               // 30 October: before the income
        ExpenseOn(t, day: 5, amount: 300m, handledThisMonth: true);                                        // 5 November: paid from that income
        ExpenseOn(t, day: 10, amount: 400m, handledThisMonth: true);                                       // 10 November: after it

        var allowance = await Get(t, Oct(28));

        Assert.Equal(8, allowance!.DaysLeft);
        Assert.Equal(3190m, allowance.UpcomingExpenses);
        Assert.Equal(1810m, allowance.Available);
        Assert.Equal(226.25m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_TheBalanceCarriesAcrossTheMonthBoundaryBeforeTheFirstIncomeOfTheMonth()
    {
        using var t = new TestDb();
        // October ended with 600 left; on 2 November nothing has come in yet and the scholarship is due on the 5th.
        AddIncome(t.Context, Alice, 2000m, Oct(5));
        AddExpense(t.Context, Alice, 1400m, Oct(20), Market);
        AddRecurring(t.Context, Alice, TransactionType.Income, 2000m, 5, Sep(1), lastGenerated: Oct(1));

        var allowance = await Get(t, new DateTime(2026, 11, 2));

        Assert.Equal(600m, allowance!.Available);
        Assert.Equal(3, allowance.DaysLeft);
        Assert.Equal(200m, allowance.DailyAmount);
        Assert.Equal(AllowanceStatus.Ok, allowance.Status);
    }

    // ---------- what counts as money on hand ----------

    [Fact]
    public async Task GetAsync_MoneyLeftFromEarlierMonthsIsStillOnHand()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 2000m, Sep(1));
        AddExpense(t.Context, Alice, 1500m, Sep(10), Market);   // September: +500
        SeedBase(t);                                            // October: +800

        Assert.Equal(1300m, (await Get(t))!.Available);
    }

    [Fact]
    public async Task GetAsync_ADeficitFromAnEarlierMonthIsCarriedToo()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 500m, Sep(1));
        AddExpense(t.Context, Alice, 800m, Sep(10), Market);    // September: -300
        SeedBase(t);

        var allowance = await Get(t);

        Assert.Equal(500m, allowance!.Available);
        Assert.Equal(62.5m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_MoneyPutIntoGoalsIsSetAside()
    {
        using var t = new TestDb();
        SeedBase(t);
        var goal = AddGoal(t.Context, Alice, 3000m);
        var bobsGoal = AddGoal(t.Context, Bob, 3000m, "Bobun");
        t.Context.GoalContributions.AddRange(
            new GoalContribution { GoalId = goal.Id, Amount = 200m, Date = Oct(2), Source = GoalContributionSource.MonthClose },
            new GoalContribution { GoalId = goal.Id, Amount = 100m, Date = Oct(3), Source = GoalContributionSource.Manual },
            new GoalContribution { GoalId = goal.Id, Amount = 100m, Date = Today, Source = GoalContributionSource.Manual },   // today's counts as well
            new GoalContribution { GoalId = bobsGoal.Id, Amount = 999m, Date = Oct(3), Source = GoalContributionSource.Manual });
        t.Context.SaveChanges();

        var allowance = await Get(t);

        Assert.Equal(400m, allowance!.Available);      // 800 - 200 - 100 - 100
        Assert.Equal(50m, allowance.DailyAmount);
        Assert.Equal(0m, allowance.SpentToday);        // putting money aside is not "spending today"
    }

    [Fact]
    public async Task GetAsync_ReservesAnExpenseThatIsDueButNotGeneratedYet()
    {
        using var t = new TestDb();
        SeedBase(t);
        ExpenseOn(t, day: 5, amount: 300m); // 5 October has passed, but the occurrence does not exist yet

        var allowance = await Get(t);

        Assert.Equal(300m, allowance!.UpcomingExpenses);
        Assert.Equal(62.5m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_DoesNotReserveAnOccurrenceThatWasAlreadyHandledThisMonth()
    {
        using var t = new TestDb();
        SeedBase(t);
        ExpenseOn(t, day: 15, amount: 300m, handledThisMonth: true); // generated early or deleted by the user: not coming again

        var allowance = await Get(t);

        Assert.Equal(0m, allowance!.UpcomingExpenses);
        Assert.Equal(100m, allowance.DailyAmount);
    }

    [Fact]
    public async Task GetAsync_DoesNotReservePausedDefinitionsOrIncomeDefinitions()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddRecurring(t.Context, Alice, TransactionType.Expense, 300m, 15, Sep(1), categoryId: Bills, isActive: false, lastGenerated: Sep(1));

        Assert.Equal(0m, (await Get(t))!.UpcomingExpenses); // the scholarship of the 20th is not an expense either
    }

    // ---------- nothing left ----------

    [Fact]
    public async Task GetAsync_IsADeficitWhenUpcomingExpensesExceedTheMoneyOnHand()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 100m, Oct(1));
        IncomeOn(t, day: 20);
        ExpenseOn(t, day: 15, amount: 300m);
        AddExpense(t.Context, Alice, 20m, Today, Coffee);

        var allowance = await Get(t);

        Assert.Equal(-200m, allowance!.Available);
        Assert.Equal(AllowanceStatus.Deficit, allowance.Status);
        Assert.Equal(0m, allowance.DailyAmount);       // never a negative "allowance"
        Assert.Equal(20m, allowance.SpentToday);
        Assert.Equal(0m, allowance.TomorrowAmount);
    }

    [Fact]
    public async Task GetAsync_ExactlyZeroAvailableIsADeficitToo()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, Oct(1));
        IncomeOn(t, day: 20);
        ExpenseOn(t, day: 15, amount: 300m);

        var allowance = await Get(t);

        Assert.Equal(0m, allowance!.Available);
        Assert.Equal(AllowanceStatus.Deficit, allowance.Status);
        Assert.Equal(0m, allowance.DailyAmount);
        Assert.Equal(0m, allowance.Shortfall);
    }

    [Fact]
    public async Task GetAsync_ADeficitCountsTodaysSpendingInTheShortfall()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 100m, Oct(1));
        IncomeOn(t, day: 20);
        ExpenseOn(t, day: 15, amount: 300m);
        AddExpense(t.Context, Alice, 20m, Today, Coffee);

        var allowance = await Get(t);

        Assert.Equal(-220m, allowance!.LeftForPeriod);
        Assert.Equal(220m, allowance.Shortfall);
    }

    [Fact]
    public async Task GetAsync_TurnsIntoADeficitAtOnceWhenTodaysSpendingExceedsEverythingAvailable()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 900m, Today, Entertainment);

        var allowance = await Get(t);

        Assert.Equal(800m, allowance!.Available);      // still measured at the start of the day
        Assert.Equal(100m, allowance.DailyAmount);
        Assert.Equal(-100m, allowance.LeftForPeriod);
        Assert.Equal(100m, allowance.Shortfall);
        Assert.Equal(AllowanceStatus.Deficit, allowance.Status);
        Assert.Equal(0m, allowance.TomorrowAmount);
    }

    [Fact]
    public async Task GetAsync_SpendingExactlyEverythingWithDaysStillToGoIsADeficit()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 800m, Today, Entertainment);

        var allowance = await Get(t);

        Assert.Equal(0m, allowance!.LeftForPeriod);
        Assert.Equal(0m, allowance.Shortfall);
        Assert.Equal(AllowanceStatus.Deficit, allowance.Status);
    }

    [Fact]
    public async Task GetAsync_SpendingAlmostEverythingIsStillOnlyOverToday()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 799.99m, Today, Entertainment);

        var allowance = await Get(t);

        Assert.Equal(0.01m, allowance!.LeftForPeriod);
        Assert.Equal(AllowanceStatus.OverToday, allowance.Status);
    }

    [Fact]
    public async Task GetAsync_SpendingTheLastShareOnTheLastDayIsNotADeficit()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 100m, Oct(1));
        IncomeOn(t, day: 13); // tomorrow: one day left, the whole 100 is today's
        AddExpense(t.Context, Alice, 100m, Today, Market);

        var allowance = await Get(t);

        Assert.Equal(1, allowance!.DaysLeft);
        Assert.Equal(0m, allowance.RemainingToday);
        Assert.Equal(AllowanceStatus.Ok, allowance.Status);
    }

    [Fact]
    public async Task GetAsync_SpendingMoreThanTheLastShareOnTheLastDayIsADeficit()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 100m, Oct(1));
        IncomeOn(t, day: 13);
        AddExpense(t.Context, Alice, 130m, Today, Market);

        var allowance = await Get(t);

        Assert.Equal(30m, allowance!.Shortfall);
        Assert.Equal(AllowanceStatus.Deficit, allowance.Status);
    }

    [Fact]
    public async Task GetAsync_AFixedExpenseTodayDoesNotCountAsSpendingEverything()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddExpense(t.Context, Alice, 700m, Today, Rent); // comes off the pool, not off today's share

        var allowance = await Get(t);

        Assert.Equal(100m, allowance!.Available);
        Assert.Equal(100m, allowance.LeftForPeriod);
        Assert.Equal(AllowanceStatus.Ok, allowance.Status);
    }

    // ---------- this month's pace against the same money ----------

    /// <summary>The home page of 17 October: 2.385,42 to last until 1 November, 159,02 a day.</summary>
    private static DailyAllowance October17(decimal available = 2385.42m, DateTime? periodEnd = null) =>
        new(periodEnd ?? new DateTime(2026, 11, 1), null, null, 15, available, 0m, 159.02m, 0m, 159.02m);

    private static readonly DateTime Oct17 = new(2026, 10, 17);

    [Fact]
    public void SpendingPace_SaysWhenTheMoneyRunsOutAtThisMonthsPace()
    {
        var warning = SpendingPace.Check(October17(), dailyPace: 233.21m, daysElapsed: 17, Oct17);

        Assert.NotNull(warning);
        // 2.385,42 / 233,21 = 10,2 days: the money lasts 10 whole days
        Assert.Equal(new DateTime(2026, 10, 27), warning.RunsOutOn);
        Assert.Equal((233.21m, 159.02m), (warning.DailyPace, warning.DailyAmount));
    }

    [Theory]
    [InlineData("150.00")]  // 15,9 days: lasts until the 1st
    [InlineData("159.02")]  // exactly the allowance
    [InlineData("50.00")]
    public void SpendingPace_SaysNothingWhenTheMoneyLastsUntilTheNextIncome(string pace) =>
        Assert.Null(SpendingPace.Check(October17(), decimal.Parse(pace, System.Globalization.CultureInfo.InvariantCulture), 17, Oct17));

    [Theory]
    [InlineData("166.97", false)] // 159,02 x 1,05 = 166,971: just under 5% above the allowance
    [InlineData("166.98", true)]  // 5% above: it shows
    [InlineData("162.00", false)] // would run out a day early (14 days), but only 2% above the allowance
    public void SpendingPace_ShowsOnlyFromFivePercentAboveTheAllowance(string pace, bool shows)
    {
        var warning = SpendingPace.Check(October17(), decimal.Parse(pace, System.Globalization.CultureInfo.InvariantCulture), 17, Oct17);

        Assert.Equal(shows, warning is not null);
    }

    [Fact]
    public void SpendingPace_RunningOutOnTheIncomeDayItselfIsNotAWarning()
    {
        // 800 until the income of the 20th; 100 a day lasts exactly 8 days (12th..19th)
        var allowance = new DailyAllowance(Oct(20), Oct(20), "KYK Bursu", 8, 800m, 0m, 100m, 0m, 100m);

        Assert.Null(SpendingPace.Check(allowance, 100m, 12, Today));
        Assert.Equal(Oct(18), SpendingPace.Check(allowance, 120m, 12, Today)!.RunsOutOn); // 6,7 days
    }

    [Fact]
    public void SpendingPace_RunningOutTodayIsAWarning()
    {
        var warning = SpendingPace.Check(October17(available: 100m), 233.21m, 17, Oct17);

        Assert.Equal(Oct17, warning!.RunsOutOn);
    }

    [Theory]
    [InlineData(2)]  // too early in the month: one purchase would set the pace
    [InlineData(1)]
    [InlineData(0)]
    public void SpendingPace_WaitsForTheFirstThreeDays(int daysElapsed) =>
        Assert.Null(SpendingPace.Check(October17(), 2000m, daysElapsed, Oct17));

    [Fact]
    public void SpendingPace_SaysNothingWithoutSpendingOrWithoutMoney()
    {
        Assert.Null(SpendingPace.Check(October17(), 0m, 17, Oct17));
        Assert.Null(SpendingPace.Check(October17(available: 0m), 233.21m, 17, Oct17));   // the allowance already shows the deficit
        Assert.Null(SpendingPace.Check(October17(available: -50m), 233.21m, 17, Oct17));
    }

    // ---------- whose data ----------

    [Fact]
    public async Task GetAsync_ReturnsNullForAUserWithoutAnyTransaction()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Bob, 9999m, Oct(1));
        IncomeOn(t, day: 20); // Alice has a definition but nothing recorded yet

        Assert.Null(await Get(t));
    }

    [Fact]
    public async Task GetAsync_UsesOnlyTheUsersOwnTransactionsAndDefinitions()
    {
        using var t = new TestDb();
        SeedBase(t);
        AddIncome(t.Context, Bob, 9999m, Oct(1));
        AddExpense(t.Context, Bob, 50m, Today, Coffee);
        IncomeOn(t, day: 13, user: Bob);
        AddRecurring(t.Context, Bob, TransactionType.Expense, 700m, 15, Sep(1), categoryId: Bills, lastGenerated: Sep(1));

        var allowance = await Get(t);

        Assert.Equal(Oct(20), allowance!.NextIncomeDate);
        Assert.Equal(0m, allowance.UpcomingExpenses);
        Assert.Equal(0m, allowance.SpentToday);
        Assert.Equal(100m, allowance.DailyAmount);
    }
}
