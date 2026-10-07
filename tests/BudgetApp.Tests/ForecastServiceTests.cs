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
