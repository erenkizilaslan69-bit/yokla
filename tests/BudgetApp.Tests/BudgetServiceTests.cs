using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class BudgetServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";

    private static DateTime Oct(int day) => new(2026, 10, day);

    // ---------- SetAsync ----------

    [Fact]
    public async Task SetAsync_CreatesALimitForAnExpenseCategory()
    {
        using var t = new TestDb();

        var budget = await new BudgetService(t.Context).SetAsync(Alice, Coffee, 500m);

        Assert.True(budget.Id > 0);
        Assert.Equal(Alice, budget.UserId);
        Assert.Equal(Coffee, budget.CategoryId);
        Assert.Equal(500m, budget.MonthlyLimit);
        Assert.Equal(BudgetLevel.None, budget.WarnedLevel);
        Assert.Null(budget.WarnedPeriod);
    }

    [Fact]
    public async Task SetAsync_RoundsTheLimitToTwoDecimals()
    {
        using var t = new TestDb();

        var budget = await new BudgetService(t.Context).SetAsync(Alice, Coffee, 99.995m);

        Assert.Equal(100m, budget.MonthlyLimit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("0.004")] // rounds to zero
    public async Task SetAsync_RejectsALimitThatIsNotPositive(string text)
    {
        using var t = new TestDb();
        var limit = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<ArgumentException>(() => new BudgetService(t.Context).SetAsync(Alice, Coffee, limit));
        Assert.Empty(t.Context.CategoryBudgets);
    }

    [Fact]
    public async Task SetAsync_RejectsIncomeAndUnknownCategories()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Alice, Income, 500m));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(Alice, 999, 500m));
        Assert.Empty(t.Context.CategoryBudgets);
    }

    [Fact]
    public async Task SetAsync_ASecondLimitForTheSameCategoryChangesTheExistingOne()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);
        var first = await service.SetAsync(Alice, Coffee, 500m);

        var second = await service.SetAsync(Alice, Coffee, 650m);

        Assert.Equal(first.Id, second.Id);
        var only = Assert.Single(t.Context.CategoryBudgets);
        Assert.Equal(650m, only.MonthlyLimit);
    }

    [Fact]
    public async Task SetAsync_ChangingTheLimitStartsTheWarningsOver()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);
        var budget = await service.SetAsync(Alice, Coffee, 500m);
        budget.WarnedPeriod = Oct(1);
        budget.WarnedLevel = BudgetLevel.Exceeded;
        t.Context.SaveChanges();

        await service.SetAsync(Alice, Coffee, 800m);

        var saved = await t.Context.CategoryBudgets.AsNoTracking().SingleAsync();
        Assert.Equal(BudgetLevel.None, saved.WarnedLevel);
        Assert.Null(saved.WarnedPeriod);
    }

    [Fact]
    public async Task SetAsync_SavingTheSameLimitAgainKeepsTheWarningState()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);
        var budget = await service.SetAsync(Alice, Coffee, 500m);
        budget.WarnedPeriod = Oct(1);
        budget.WarnedLevel = BudgetLevel.Approaching;
        t.Context.SaveChanges();

        await service.SetAsync(Alice, Coffee, 500m);

        var saved = await t.Context.CategoryBudgets.AsNoTracking().SingleAsync();
        Assert.Equal(BudgetLevel.Approaching, saved.WarnedLevel);
        Assert.Equal(Oct(1), saved.WarnedPeriod);
    }

    [Fact]
    public async Task SetAsync_LimitsAreSeparatePerUser()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);

        await service.SetAsync(Alice, Coffee, 500m);
        await service.SetAsync(Bob, Coffee, 100m);

        Assert.Equal(2, t.Context.CategoryBudgets.Count());
        Assert.Equal(500m, t.Context.CategoryBudgets.Single(b => b.UserId == Alice).MonthlyLimit);
    }

    // ---------- GetAsync / DeleteAsync ----------

    [Fact]
    public async Task GetAsync_ReturnsTheOwnLimitWithItsCategoryAndNothingForOthers()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);
        var bobs = await service.SetAsync(Bob, Coffee, 100m);

        Assert.Null(await service.GetAsync(Alice, bobs.Id));
        var found = await service.GetAsync(Bob, bobs.Id);
        Assert.Equal("Kafe/Kahve", found!.Category!.Name);
        Assert.Null(await service.GetAsync(Bob, 999));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheOwnLimitOnly()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);
        var alices = await service.SetAsync(Alice, Coffee, 500m);
        var bobs = await service.SetAsync(Bob, Coffee, 100m);

        Assert.False(await service.DeleteAsync(Alice, bobs.Id));
        Assert.Equal(2, t.Context.CategoryBudgets.Count());

        Assert.True(await service.DeleteAsync(Alice, alices.Id));
        Assert.Equal(bobs.Id, Assert.Single(t.Context.CategoryBudgets).Id);
        Assert.False(await service.DeleteAsync(Alice, alices.Id));
    }

    [Fact]
    public async Task DeleteAsync_KeepsTheTransactionsOfThatCategory()
    {
        using var t = new TestDb();
        var service = new BudgetService(t.Context);
        var budget = await service.SetAsync(Alice, Coffee, 500m);
        AddExpense(t.Context, Alice, 80m, Oct(3), Coffee);

        await service.DeleteAsync(Alice, budget.Id);

        Assert.Single(t.Context.Transactions);
    }

    // ---------- GetProgressAsync ----------

    [Fact]
    public async Task GetProgressAsync_ReturnsAnEmptyListWithoutLimits()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 80m, Oct(3), Coffee);

        Assert.Empty(await new BudgetService(t.Context).GetProgressAsync(Alice, 2026, 10));
    }

    [Fact]
    public async Task GetProgressAsync_SumsTheCategorysExpensesOfThatMonthOnly()
    {
        using var t = new TestDb();
        AddBudget(t.Context, Alice, Coffee, 500m);
        AddExpense(t.Context, Alice, 80m, Oct(3), Coffee);
        AddExpense(t.Context, Alice, 120m, Oct(31), Coffee);
        AddExpense(t.Context, Alice, 900m, new DateTime(2026, 9, 30), Coffee);   // previous month
        AddExpense(t.Context, Alice, 900m, new DateTime(2026, 11, 1), Coffee);   // next month
        AddExpense(t.Context, Alice, 700m, Oct(5), Market);                      // other category
        AddExpense(t.Context, Bob, 700m, Oct(5), Coffee);                        // someone else

        var october = await new BudgetService(t.Context).GetProgressAsync(Alice, 2026, 10);
        var september = await new BudgetService(t.Context).GetProgressAsync(Alice, 2026, 9);

        Assert.Equal(200m, Assert.Single(october).Spent);
        Assert.Equal(900m, Assert.Single(september).Spent);
    }

    [Fact]
    public async Task GetProgressAsync_ShowsALimitWithoutAnySpendingAsZero()
    {
        using var t = new TestDb();
        AddBudget(t.Context, Alice, Coffee, 500m);

        var progress = Assert.Single(await new BudgetService(t.Context).GetProgressAsync(Alice, 2026, 10));

        Assert.Equal(0m, progress.Spent);
        Assert.Equal(0m, progress.Percent);
        Assert.Equal(500m, progress.Remaining);
        Assert.Equal(BudgetLevel.None, progress.Level);
        Assert.Equal("Kafe/Kahve", progress.Budget.Category!.Name);
    }

    [Fact]
    public async Task GetProgressAsync_CountsExpensesGeneratedFromRecurringDefinitions()
    {
        using var t = new TestDb();
        AddBudget(t.Context, Alice, Bills, 500m);
        AddGeneratedExpense(t.Context, Alice, 299m, Oct(10), Bills, "İnternet");
        AddExpense(t.Context, Alice, 100m, Oct(15), Bills, "Su");

        Assert.Equal(399m, Assert.Single(await new BudgetService(t.Context).GetProgressAsync(Alice, 2026, 10)).Spent);
    }

    [Fact]
    public async Task GetProgressAsync_ListsTheMostUsedLimitFirst()
    {
        using var t = new TestDb();
        AddBudget(t.Context, Alice, Coffee, 500m);          // 10%
        AddBudget(t.Context, Alice, EatingOut, 400m);       // 120%
        AddBudget(t.Context, Alice, Entertainment, 200m);   // 50%
        AddExpense(t.Context, Alice, 50m, Oct(3), Coffee);
        AddExpense(t.Context, Alice, 480m, Oct(3), EatingOut);
        AddExpense(t.Context, Alice, 100m, Oct(3), Entertainment);
        AddBudget(t.Context, Bob, Market, 10m);

        var names = (await new BudgetService(t.Context).GetProgressAsync(Alice, 2026, 10))
            .Select(p => p.Budget.Category!.Name).ToArray();

        Assert.Equal(["Dışarıda Yemek", "Eğlence", "Kafe/Kahve"], names);
    }

    // ---------- BudgetProgress ----------

    [Theory]
    [InlineData("0", BudgetLevel.None)]
    [InlineData("400", BudgetLevel.None)]            // exactly 80%
    [InlineData("400.01", BudgetLevel.Approaching)]
    [InlineData("500", BudgetLevel.Approaching)]     // exactly 100%
    [InlineData("500.01", BudgetLevel.Exceeded)]
    [InlineData("900", BudgetLevel.Exceeded)]
    public void BudgetProgress_LevelFollowsTheEightyAndHundredPercentThresholds(string spent, BudgetLevel expected)
    {
        var progress = new BudgetProgress(
            new CategoryBudget { MonthlyLimit = 500m },
            decimal.Parse(spent, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, progress.Level);
    }

    [Fact]
    public void BudgetProgress_PercentCanExceedOneHundredAndRemainingIsNeverNegative()
    {
        var over = new BudgetProgress(new CategoryBudget { MonthlyLimit = 400m }, 500m);
        var under = new BudgetProgress(new CategoryBudget { MonthlyLimit = 400m }, 100m);

        Assert.Equal(125m, over.Percent);
        Assert.Equal(0m, over.Remaining);
        Assert.Equal(25m, under.Percent);
        Assert.Equal(300m, under.Remaining);
    }
}
