using BudgetApp.Services;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class AnalysisServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Today = new(2026, 10, 15);

    private static AnalysisService NewService(TestDb t) => new(t.Context, new ForecastService(t.Context));

    private static Task<MonthlyAnalysis> Analyze(TestDb t, int year = 2026, int month = 10, string user = Alice) =>
        NewService(t).GetMonthlyAnalysisAsync(user, year, month, Today);

    private static DateTime Oct(int day) => new(2026, 10, day);
    private static DateTime Sep(int day) => new(2026, 9, day);

    // ---------- category totals ----------

    [Fact]
    public async Task Categories_SumExpensesPerCategoryLargestFirst()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, Oct(2), Market);
        AddExpense(t.Context, Alice, 50m, Oct(9), Market);
        AddExpense(t.Context, Alice, 400m, Oct(3), Rent);
        AddExpense(t.Context, Alice, 75m, Oct(4), Coffee);

        var analysis = await Analyze(t);

        Assert.Equal(["Kira/Yurt", "Market", "Kafe/Kahve"], analysis.Categories.Select(c => c.Name).ToArray());
        Assert.Equal([400m, 150m, 75m], analysis.Categories.Select(c => c.Amount).ToArray());
        Assert.Equal(625m, analysis.TotalExpense);
    }

    [Fact]
    public async Task Categories_ExcludeIncomeOtherMonthsAndOtherUsers()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct(1));
        AddExpense(t.Context, Alice, 100m, Oct(2), Market);
        AddExpense(t.Context, Alice, 900m, Sep(30), Market);
        AddExpense(t.Context, Alice, 900m, new DateTime(2026, 11, 1), Market);
        AddExpense(t.Context, Bob, 900m, Oct(2), Market);

        var analysis = await Analyze(t);

        var only = Assert.Single(analysis.Categories);
        Assert.Equal(100m, only.Amount);
        Assert.Equal(100m, analysis.TotalExpense);
    }

    [Fact]
    public async Task EmptyMonth_ReturnsEmptyAnalysisWithoutAdvice()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 5000m, Oct(1));

        var analysis = await Analyze(t);

        Assert.Empty(analysis.Categories);
        Assert.Empty(analysis.TopItems);
        Assert.Empty(analysis.Changes);
        Assert.Empty(analysis.Advice);
        Assert.Equal(0m, analysis.TotalExpense);
        Assert.Equal(2026, analysis.Year);
        Assert.Equal(10, analysis.Month);
    }

    // ---------- top items ----------

    [Fact]
    public async Task TopItems_GroupByItemNameWithTotalAndCount()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 30m, Oct(2), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(5), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(9), Market, "Süt");
        AddExpense(t.Context, Alice, 200m, Oct(3), Market, "Kulaklık");

        var analysis = await Analyze(t);

        Assert.Equal(["Kulaklık", "Süt"], analysis.TopItems.Select(i => i.ItemName).ToArray());
        var milk = analysis.TopItems[1];
        Assert.Equal(90m, milk.Amount);
        Assert.Equal(3, milk.Count);
    }

    [Fact]
    public async Task TopItems_IgnoreExpensesWithoutItemName()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 500m, Oct(2), Market);              // no item name
        AddExpense(t.Context, Alice, 500m, Oct(3), Market, "  ");        // blank item name
        AddExpense(t.Context, Alice, 10m, Oct(4), Market, "Ekmek");

        var analysis = await Analyze(t);

        var only = Assert.Single(analysis.TopItems);
        Assert.Equal("Ekmek", only.ItemName);
    }

    [Fact]
    public async Task TopItems_AreLimitedToTenLargest()
    {
        using var t = new TestDb();
        for (var i = 1; i <= 12; i++)
            AddExpense(t.Context, Alice, i * 10m, Oct(2), Market, $"Ürün {i}");

        var analysis = await Analyze(t);

        Assert.Equal(10, analysis.TopItems.Count);
        Assert.Equal("Ürün 12", analysis.TopItems[0].ItemName);
        Assert.Equal("Ürün 3", analysis.TopItems[^1].ItemName); // items 1 and 2 are cut off
    }

    [Fact]
    public async Task TopItems_ExcludeIncomeOtherMonthsAndOtherUsers()
    {
        using var t = new TestDb();
        Add(t.Context, Alice, BudgetApp.Models.TransactionType.Income, 999m, Oct(2), Income, "Burs");
        AddExpense(t.Context, Alice, 999m, Sep(2), Market, "Eski");
        AddExpense(t.Context, Bob, 999m, Oct(2), Market, "Bobun");
        AddExpense(t.Context, Alice, 5m, Oct(2), Market, "Benim");

        var analysis = await Analyze(t);

        Assert.Equal(["Benim"], analysis.TopItems.Select(i => i.ItemName).ToArray());
    }

    // ---------- changes vs previous month ----------

    [Fact]
    public async Task Changes_ShowDeltaAndPercentAgainstPreviousMonth()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, Sep(5), Market);
        AddExpense(t.Context, Alice, 150m, Oct(5), Market);

        var analysis = await Analyze(t);

        var market = Assert.Single(analysis.Changes);
        Assert.Equal("Market", market.Name);
        Assert.Equal(150m, market.Current);
        Assert.Equal(100m, market.Previous);
        Assert.Equal(50m, market.Delta);
        Assert.Equal(50m, market.Percent);
        Assert.Equal(100m, analysis.PreviousTotalExpense);
    }

    [Fact]
    public async Task Changes_PercentIsNullForNewCategory()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 80m, Oct(5), Coffee);

        var change = Assert.Single((await Analyze(t)).Changes);

        Assert.Equal(0m, change.Previous);
        Assert.Equal(80m, change.Delta);
        Assert.Null(change.Percent);
    }

    [Fact]
    public async Task Changes_IncludeCategoriesThatDroppedToZero()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 200m, Sep(5), Transport);
        AddExpense(t.Context, Alice, 10m, Oct(5), Market);

        var analysis = await Analyze(t);

        var transport = analysis.Changes.Single(c => c.Name == "Ulaşım");
        Assert.Equal(0m, transport.Current);
        Assert.Equal(-200m, transport.Delta);
        Assert.Equal(-100m, transport.Percent);
    }

    [Fact]
    public async Task Changes_AreSortedByLargestIncreaseFirst()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, Sep(5), Market);
        AddExpense(t.Context, Alice, 110m, Oct(5), Market);      // +10
        AddExpense(t.Context, Alice, 100m, Sep(5), Transport);
        AddExpense(t.Context, Alice, 40m, Oct(5), Transport);    // -60
        AddExpense(t.Context, Alice, 300m, Oct(5), Coffee);      // +300

        var names = (await Analyze(t)).Changes.Select(c => c.Name).ToArray();

        Assert.Equal(["Kafe/Kahve", "Market", "Ulaşım"], names);
    }

    [Fact]
    public async Task Changes_InJanuaryCompareWithDecemberOfPreviousYear()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, new DateTime(2025, 12, 20), Market);
        AddExpense(t.Context, Alice, 120m, new DateTime(2026, 1, 5), Market);

        var analysis = await NewService(t).GetMonthlyAnalysisAsync(Alice, 2026, 1, new DateTime(2026, 1, 20));

        Assert.Equal(100m, Assert.Single(analysis.Changes).Previous);
    }

    [Fact]
    public async Task Changes_IgnoreOtherUsers()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Bob, 900m, Sep(5), Market);
        AddExpense(t.Context, Alice, 100m, Oct(5), Market);

        var change = Assert.Single((await Analyze(t)).Changes);

        Assert.Equal(0m, change.Previous);
    }

    // ---------- advice ----------

    /// <summary>September: income 2000, expenses 1500 (+500 per month). October: coffee 400 (was 100).</summary>
    private static void SeedCoffeeIncrease(TestDb t)
    {
        AddIncome(t.Context, Alice, 2000m, Sep(1));
        AddExpense(t.Context, Alice, 1400m, Sep(2), Market);
        AddExpense(t.Context, Alice, 100m, Sep(3), Coffee);
        AddExpense(t.Context, Alice, 400m, Oct(3), Coffee);
    }

    [Fact]
    public async Task Advice_HalvingMostIncreasedUnnecessaryCategoryReachesGoalMonthsEarlier()
    {
        using var t = new TestDb();
        SeedCoffeeIncrease(t);
        AddGoal(t.Context, Alice, 3000m); // base: 3000 / 500 = 6 months; saving 200 more: 3000 / 700 -> 5 months

        var analysis = await Analyze(t);

        Assert.Contains("Kafe/Kahve", analysis.Advice[0]);
        Assert.Contains("1 ay erken", analysis.Advice[0]);
    }

    [Fact]
    public async Task Advice_WithoutGoalSaysHowMuchStaysInThePocket()
    {
        using var t = new TestDb();
        SeedCoffeeIncrease(t);

        var analysis = await Analyze(t);

        Assert.Contains("Kafe/Kahve", analysis.Advice[0]);
        Assert.Contains("200,00", analysis.Advice[0]);
        Assert.Contains("cebinde kalır", analysis.Advice[0]);
    }

    [Fact]
    public async Task Advice_PicksTheUnnecessaryCategoryWithTheLargestIncrease()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, Sep(2), Entertainment);
        AddExpense(t.Context, Alice, 150m, Oct(2), Entertainment);   // +50
        AddExpense(t.Context, Alice, 100m, Sep(2), EatingOut);
        AddExpense(t.Context, Alice, 400m, Oct(2), EatingOut);       // +300
        AddExpense(t.Context, Alice, 100m, Sep(2), Market);
        AddExpense(t.Context, Alice, 5000m, Oct(2), Market);         // biggest increase, but not unnecessary

        var analysis = await Analyze(t);

        Assert.Contains("Dışarıda Yemek", analysis.Advice[0]);
    }

    [Fact]
    public async Task Advice_FallsBackToLargestUnnecessaryCategoryWhenNothingIncreased()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 300m, Sep(2), Coffee);
        AddExpense(t.Context, Alice, 300m, Oct(2), Coffee);

        var analysis = await Analyze(t);

        Assert.Contains("Kafe/Kahve", analysis.Advice[0]);
    }

    [Fact]
    public async Task Advice_MentionsFrequentlyBoughtItem()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 30m, Oct(2), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(5), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(9), Market, "Süt");

        var analysis = await Analyze(t);

        Assert.Contains(analysis.Advice, a => a.Contains("Süt için bu ay 3 kez toplam 90,00"));
    }

    [Fact]
    public async Task Advice_DoesNotMentionItemsBoughtOnlyTwice()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 30m, Oct(2), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(5), Market, "Süt");

        var analysis = await Analyze(t);

        Assert.DoesNotContain(analysis.Advice, a => a.Contains("Süt için"));
    }

    [Fact]
    public async Task Advice_AlwaysHasBetweenTwoAndThreeEntriesWhenThereIsSpending()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, Oct(2), Market); // no unnecessary category, no repeated item, no previous month

        var analysis = await Analyze(t);

        Assert.InRange(analysis.Advice.Count, 2, 3);
        Assert.Contains(analysis.Advice, a => a.Contains("%100") && a.Contains("Market"));
    }

    [Fact]
    public async Task Advice_IsCappedAtThreeEntries()
    {
        using var t = new TestDb();
        SeedCoffeeIncrease(t);
        AddExpense(t.Context, Alice, 30m, Oct(2), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(5), Market, "Süt");
        AddExpense(t.Context, Alice, 30m, Oct(9), Market, "Süt");
        AddGoal(t.Context, Alice, 3000m);

        var analysis = await Analyze(t);

        Assert.Equal(3, analysis.Advice.Count);
    }

    [Fact]
    public async Task Advice_UsesOnlyTheUsersOwnData()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Bob, 5000m, Oct(2), Coffee, "Latte");
        AddExpense(t.Context, Bob, 5000m, Oct(3), Coffee, "Latte");
        AddExpense(t.Context, Bob, 5000m, Oct(4), Coffee, "Latte");
        AddExpense(t.Context, Alice, 10m, Oct(2), Market);

        var analysis = await Analyze(t);

        Assert.DoesNotContain(analysis.Advice, a => a.Contains("Kafe/Kahve") || a.Contains("Latte"));
    }
}
