using BudgetApp.Services;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class ForecastServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";

    // ---------- GetProjectedBalanceAsync ----------

    [Fact]
    public async Task GetProjectedBalanceAsync_UsesFixedAsIsAndScalesVariable()
    {
        using var t = new TestDb();
        // 7 October, 31-day month: 4000 - 2200 - 250 * 31 / 7 = 692.857...
        AddIncome(t.Context, Alice, 4000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 2200m, new DateTime(2026, 10, 1), Rent);
        AddExpense(t.Context, Alice, 250m, new DateTime(2026, 10, 5), Market);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 7));

        Assert.NotNull(projection);
        Assert.Equal(4000m, projection.Income);
        Assert.Equal(2200m, projection.FixedExpenses);
        Assert.Equal(250m, projection.VariableExpenses);
        Assert.Equal(692.857m, Math.Round(projection.Projected, 3));
        Assert.Equal(693m, Math.Round(projection.Projected));
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_GivesTheDailyPaceOfVariableSpendingSoFar()
    {
        using var t = new TestDb();
        // 17 October: 3.964,58 of variable spending is 233,21 a day; rent and recurring items do not count.
        AddIncome(t.Context, Alice, 9500m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 3000m, new DateTime(2026, 10, 1), Rent);
        AddGeneratedExpense(t.Context, Alice, 189.99m, new DateTime(2026, 10, 12), Market, "Netflix");
        AddExpense(t.Context, Alice, 3964.58m, new DateTime(2026, 10, 9), Entertainment);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 17));

        Assert.Equal(17, projection!.DaysElapsed);
        Assert.Equal(233.21m, projection.DailyPace);
    }

    [Fact]
    public void DailyPace_IsZeroBeforeAnyDayHasPassed()
    {
        Assert.Equal(0m, new BalanceProjection(100m, 0m, 50m, 0m).DailyPace);
        Assert.Equal(25m, new BalanceProjection(100m, 0m, 50m, 0m, DaysElapsed: 2).DailyPace);
        Assert.Equal(16.67m, new BalanceProjection(100m, 0m, 50m, 0m, DaysElapsed: 3).DailyPace); // rounded to the kuruş
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_TreatsAllFixedCategoriesAsFixed()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 1000m, new DateTime(2026, 10, 1), Rent);
        AddExpense(t.Context, Alice, 300m, new DateTime(2026, 10, 2), Bills);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 10));

        Assert.Equal(1300m, projection!.FixedExpenses);
        Assert.Equal(0m, projection.VariableExpenses);
        Assert.Equal(1700m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_CanBeNegative()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 600m, new DateTime(2026, 10, 3), EatingOut); // 600 * 31 / 10 = 1860 > income

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 10));

        Assert.True(projection!.Projected < 0);
        Assert.Equal(1000m - 600m * 31m / 10m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_ReturnsNullWhenNoTransactionsThisMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 9, 1)); // only last month

        Assert.Null(await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 10)));
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_WorksWithIncomeOnly()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1500m, new DateTime(2026, 10, 1));

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 5));

        Assert.Equal(1500m, projection!.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_IgnoresOtherMonthsAndOtherUsers()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 5000m, new DateTime(2026, 9, 30));
        AddExpense(t.Context, Bob, 5000m, new DateTime(2026, 10, 2));

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 10, 10));

        Assert.Equal(1000m, projection!.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_UsesDaysOfTheCurrentMonth()
    {
        using var t = new TestDb();
        // February 2026 has 28 days: 140 * 28 / 14 = 280
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 2, 1));
        AddExpense(t.Context, Alice, 140m, new DateTime(2026, 2, 10));

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, new DateTime(2026, 2, 14));

        Assert.Equal(720m, projection!.Projected);
    }

    // ---------- GetProjectedBalanceAsync: recurring definitions ----------

    private static readonly DateTime Oct11 = new(2026, 10, 11);
    private static readonly DateTime Oct1 = new(2026, 10, 1);

    [Fact]
    public async Task GetProjectedBalanceAsync_KnownUpcomingIncomeTurnsAFalseDeficitIntoASurplus()
    {
        using var t = new TestDb();
        // 11 October: 5.500 income so far, rent 3.000, everyday spending 1.100 (scaled: 1.100 * 31 / 11 = 3.100).
        AddIncome(t.Context, Alice, 5500m, Oct1);
        AddExpense(t.Context, Alice, 3000m, Oct1, Rent);
        AddExpense(t.Context, Alice, 1100m, new DateTime(2026, 10, 5), Market);
        var service = new ForecastService(t.Context);

        var before = await service.GetProjectedBalanceAsync(Alice, Oct11);
        Assert.Equal(-600m, before!.Projected); // looks like a deficit...

        // ...but the part-time pay arrives on the 20th every month.
        AddRecurring(t.Context, Alice, BudgetApp.Models.TransactionType.Income, 1500m, day: 20, start: Oct1);

        var after = await service.GetProjectedBalanceAsync(Alice, Oct11);
        Assert.Equal(1500m, after!.PendingIncome);
        Assert.Equal(0m, after.PendingExpenses);
        Assert.Equal(5500m, after.Income);
        Assert.Equal(7000m, after.KnownIncome);
        Assert.Equal(900m, after.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_AddsUpcomingRecurringExpensesAsTheyAre()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct1);
        AddRecurring(t.Context, Alice, BudgetApp.Models.TransactionType.Expense, 190m, day: 12, start: Oct1, categoryId: Bills);
        AddRecurring(t.Context, Alice, BudgetApp.Models.TransactionType.Expense, 200m, day: 18, start: Oct1, categoryId: Transport);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, Oct11);

        Assert.Equal(390m, projection!.PendingExpenses);    // not multiplied by 31/11, whatever the category
        Assert.Equal(4610m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_DoesNotScaleExpensesThatWereGeneratedFromARecurringDefinition()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct1);
        AddGeneratedExpense(t.Context, Alice, 200m, new DateTime(2026, 10, 3), Transport); // monthly pass: variable category
        AddExpense(t.Context, Alice, 110m, new DateTime(2026, 10, 5), Market);             // scaled: 110 * 31 / 11 = 310

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, Oct11);

        Assert.Equal(200m, projection!.FixedExpenses);
        Assert.Equal(110m, projection.VariableExpenses);
        Assert.Equal(4490m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_DoesNotCountAnOccurrenceTwiceOnceItWasGenerated()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct1);
        AddRecurring(t.Context, Alice, BudgetApp.Models.TransactionType.Expense, 3000m, day: 1, start: Oct1, lastGenerated: Oct1);
        AddGeneratedExpense(t.Context, Alice, 3000m, Oct1, Rent);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, Oct11);

        Assert.Equal(0m, projection!.PendingExpenses);
        Assert.Equal(2000m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_IgnoresDefinitionsThatWillNotProduceAnythingThisMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct1);
        var expense = BudgetApp.Models.TransactionType.Expense;
        AddRecurring(t.Context, Alice, expense, 100m, day: 20, start: Oct1, isActive: false);                       // paused
        AddRecurring(t.Context, Alice, expense, 100m, day: 20, start: new DateTime(2026, 11, 1));                   // starts next month
        AddRecurring(t.Context, Alice, expense, 100m, day: 20, start: Oct1, end: new DateTime(2026, 10, 15));       // ended before its day
        AddRecurring(t.Context, Alice, expense, 100m, day: 5, start: Oct11);                                        // this month's day passed before it started
        AddRecurring(t.Context, Bob, expense, 100m, day: 20, start: Oct1);                                          // someone else's

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, Oct11);

        Assert.Equal(0m, projection!.PendingExpenses);
        Assert.Equal(5000m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_IsCalculatedFromUpcomingOccurrencesAloneWhenNothingIsRecordedYet()
    {
        using var t = new TestDb();
        AddRecurring(t.Context, Alice, BudgetApp.Models.TransactionType.Income, 2000m, day: 20, start: Oct1);
        AddRecurring(t.Context, Alice, BudgetApp.Models.TransactionType.Expense, 500m, day: 25, start: Oct1);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, Oct11);

        Assert.NotNull(projection);
        Assert.Equal(0m, projection.Income);
        Assert.Equal(2000m, projection.PendingIncome);
        Assert.Equal(500m, projection.PendingExpenses);
        Assert.Equal(1500m, projection.Projected);
    }

    [Fact]
    public async Task GetProjectedBalanceAsync_HasNoPendingAmountsWithoutRecurringDefinitions()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Oct1);

        var projection = await new ForecastService(t.Context).GetProjectedBalanceAsync(Alice, Oct11);

        Assert.Equal(0m, projection!.PendingIncome);
        Assert.Equal(0m, projection.PendingExpenses);
        Assert.Equal(1000m, projection.KnownIncome);
    }

    // ---------- GetGoalForecastAsync ----------

    private static readonly DateTime Today = new(2026, 10, 15);

    private static void AddMonth(TestDb t, int month, decimal income, decimal expense, string user = Alice)
    {
        AddIncome(t.Context, user, income, new DateTime(2026, month, 1));
        if (expense > 0)
            AddExpense(t.Context, user, expense, new DateTime(2026, month, 2));
    }

    [Fact]
    public async Task GetGoalForecastAsync_ReturnsNullWithoutActiveGoal()
    {
        using var t = new TestDb();
        AddMonth(t, 9, 2000m, 1000m);

        Assert.Null(await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today));
    }

    [Fact]
    public async Task GetGoalForecastAsync_ReturnsNullWithoutHistory()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 3000m);
        // Only the current month has data; no completed month to average.
        AddMonth(t, 10, 2000m, 500m);

        Assert.Null(await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today));
    }

    [Fact]
    public async Task GetGoalForecastAsync_RoundsMonthsUpAndComputesDate()
    {
        using var t = new TestDb();
        var goal = AddGoal(t.Context, Alice, 3000m, saved: 600m); // 2400 missing
        AddMonth(t, 9, 2000m, 1200m);  // +800
        AddMonth(t, 8, 2000m, 1200m);  // +800
        AddMonth(t, 7, 2000m, 1200m);  // +800  => average 800 => 3 months

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.NotNull(forecast);
        Assert.Equal(goal.Id, forecast.Goal.Id);
        Assert.Equal(GoalForecastStatus.Reachable, forecast.Status);
        Assert.Equal(3, forecast.Months);
        Assert.Equal(new DateTime(2027, 1, 1), forecast.ReachDate);
    }

    [Fact]
    public async Task GetGoalForecastAsync_RoundsUpPartialMonth()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 1000m);
        AddMonth(t, 9, 1500m, 900m); // +600 => 1000 / 600 = 1.67 => 2

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.Equal(2, forecast!.Months);
    }

    [Fact]
    public async Task GetGoalForecastAsync_AveragesOnlyLastThreeCompletedMonths()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 3000m);
        AddMonth(t, 9, 2000m, 1000m); // +1000
        AddMonth(t, 8, 2000m, 1000m); // +1000
        AddMonth(t, 7, 2000m, 1000m); // +1000
        AddMonth(t, 6, 2000m, 9000m); // -7000, older than 3 months: must be ignored

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.Equal(GoalForecastStatus.Reachable, forecast!.Status);
        Assert.Equal(3, forecast.Months);
    }

    [Fact]
    public async Task GetGoalForecastAsync_NegativeMonthsLowerTheAverage()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 1000m);
        AddMonth(t, 9, 2000m, 1000m); // +1000
        AddMonth(t, 8, 1000m, 1600m); // -600  => average +200 => 5 months

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.Equal(GoalForecastStatus.Reachable, forecast!.Status);
        Assert.Equal(5, forecast.Months);
    }

    [Fact]
    public async Task GetGoalForecastAsync_SkipsMonthsWithoutTransactions()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 1000m);
        AddMonth(t, 9, 2000m, 1000m); // +1000; August and July have no data and are not averaged as zero

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.Equal(1, forecast!.Months);
    }

    [Theory]
    [InlineData(1000, 1000)] // average exactly zero
    [InlineData(1000, 1500)] // average negative
    public async Task GetGoalForecastAsync_NeedsCutbackWhenAverageIsNotPositive(int income, int expense)
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 1000m);
        AddMonth(t, 9, income, expense);

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.Equal(GoalForecastStatus.NeedsCutback, forecast!.Status);
        Assert.Null(forecast.ReachDate);
    }

    [Fact]
    public async Task GetGoalForecastAsync_ExtraMonthlySavingShortensTheForecast()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 3000m);
        AddMonth(t, 9, 2000m, 1000m); // +1000 => 3 months

        var service = new ForecastService(t.Context);
        var baseline = await service.GetGoalForecastAsync(Alice, Today);
        var withSaving = await service.GetGoalForecastAsync(Alice, Today, extraMonthlySaving: 500m); // +1500 => 2 months

        Assert.Equal(3, baseline!.Months);
        Assert.Equal(2, withSaving!.Months);
    }

    [Fact]
    public async Task GetGoalForecastAsync_ExtraSavingCanTurnCutbackIntoReachable()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 1000m);
        AddMonth(t, 9, 1000m, 1100m); // -100

        var withSaving = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today, extraMonthlySaving: 300m);

        Assert.Equal(GoalForecastStatus.Reachable, withSaving!.Status); // +200 => 5 months
        Assert.Equal(5, withSaving.Months);
    }

    [Fact]
    public async Task GetGoalForecastAsync_UsesOnlyOwnDataAndGoal()
    {
        using var t = new TestDb();
        AddGoal(t.Context, Alice, 1000m);
        AddMonth(t, 9, 2000m, 1000m);          // Alice +1000
        AddMonth(t, 9, 9000m, 0m, Bob);        // Bob must not influence the average
        AddGoal(t.Context, Bob, 50m, "Bobun");

        var forecast = await new ForecastService(t.Context).GetGoalForecastAsync(Alice, Today);

        Assert.Equal(1, forecast!.Months);
        Assert.Equal("Kulaklık", forecast.Goal.Name);
    }
}
