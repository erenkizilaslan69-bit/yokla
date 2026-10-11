using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Tests;

public class DemoDataSeederTests
{
    private const string Demo = "demo-user";
    private const string Other = "someone-else";
    private static readonly DateTime Today = new(2026, 10, 15);

    private static async Task<TestDb> SeededDb(DateTime? today = null)
    {
        var t = new TestDb();
        await DemoDataSeeder.SeedDataAsync(t.Context, Demo, today ?? Today);
        return t;
    }

    // ---------- shape of the data ----------

    [Fact]
    public async Task SeedDataAsync_CoversTheLastThreeMonthsAndTheCurrentOne()
    {
        using var t = await SeededDb();

        var months = await t.Context.Transactions
            .Select(x => new { x.Date.Year, x.Date.Month }).Distinct().ToListAsync();

        Assert.Equal(4, months.Count);
        foreach (var m in new[] { (2026, 7), (2026, 8), (2026, 9), (2026, 10) })
            Assert.Contains(months, x => x.Year == m.Item1 && x.Month == m.Item2);
    }

    [Theory]
    [InlineData(2026, 10, 3)]
    [InlineData(2026, 10, 8)]
    [InlineData(2026, 10, 15)]
    [InlineData(2026, 10, 28)]
    [InlineData(2026, 11, 1)]
    [InlineData(2027, 1, 2)]
    public async Task SeedDataAsync_NeverCreatesFutureTransactionsAndKeepsDataConsistent(int year, int month, int day)
    {
        var today = new DateTime(year, month, day);
        using var t = await SeededDb(today);

        var all = await t.Context.Transactions.Include(x => x.Category).ToListAsync();

        Assert.NotEmpty(all);
        Assert.All(all, x =>
        {
            Assert.True(x.Date <= today, $"{x.Date:yyyy-MM-dd} is after today");
            Assert.True(x.Amount > 0);
            Assert.Equal(Demo, x.UserId);
            Assert.Equal(x.Category!.Type, x.Type);
        });
        Assert.Equal(all.Count, t.Context.Transactions.Count());
    }

    [Fact]
    public async Task SeedDataAsync_UsesRealisticStudentCategoriesAndItemNames()
    {
        using var t = await SeededDb();

        var categories = await t.Context.Transactions.Select(x => x.Category!.Name).Distinct().ToListAsync();
        var items = await t.Context.Transactions.Where(x => x.ItemName != null).Select(x => x.ItemName!).Distinct().ToListAsync();

        foreach (var name in new[]
                 { "KYK Bursu", "Aile Desteği", "Kira/Yurt", "Faturalar", "Market", "Kafe/Kahve", "Ulaşım", "Dışarıda Yemek", "Eğlence" })
            Assert.Contains(name, categories);
        Assert.Contains("Süt", items);
        Assert.Contains("Latte", items);
    }

    [Fact]
    public async Task SeedDataAsync_ReturnsTrueWhenSeedingAndFalseOnSecondRun()
    {
        using var t = new TestDb();

        Assert.True(await DemoDataSeeder.SeedDataAsync(t.Context, Demo, Today));
        var transactions = t.Context.Transactions.Count();
        var contributions = t.Context.GoalContributions.Count();

        Assert.False(await DemoDataSeeder.SeedDataAsync(t.Context, Demo, Today));
        Assert.Equal(transactions, t.Context.Transactions.Count());
        Assert.Equal(contributions, t.Context.GoalContributions.Count());
        Assert.Single(t.Context.Goals);
    }

    [Fact]
    public async Task SeedDataAsync_DoesNothingForAUserWhoAlreadyHasTransactions()
    {
        using var t = new TestDb();
        TestData.AddExpense(t.Context, Demo, 10m, new DateTime(2026, 10, 1));

        Assert.False(await DemoDataSeeder.SeedDataAsync(t.Context, Demo, Today));
        Assert.Single(t.Context.Transactions);
        Assert.Empty(t.Context.Goals);
    }

    [Fact]
    public async Task SeedDataAsync_LeavesOtherUsersUntouched()
    {
        using var t = new TestDb();
        TestData.AddExpense(t.Context, Other, 10m, new DateTime(2026, 10, 1));
        TestData.AddGoal(t.Context, Other, 100m, "Başkasının");

        await DemoDataSeeder.SeedDataAsync(t.Context, Demo, Today);

        Assert.Single(t.Context.Transactions, x => x.UserId == Other);
        Assert.Single(t.Context.Goals, g => g.UserId == Other);
        Assert.Empty(t.Context.MonthCloses.Where(m => m.UserId == Other));
    }

    // ---------- goal and month closes ----------

    [Fact]
    public async Task SeedDataAsync_CreatesTheGoalAndClosesTheTwoOldestMonths()
    {
        using var t = await SeededDb();

        var goal = await t.Context.Goals.SingleAsync();
        Assert.Equal("Kulaklık", goal.Name);
        Assert.Equal(Demo, goal.UserId);
        Assert.False(goal.IsCompleted);
        Assert.Null(goal.CompletedAt);

        var closes = await t.Context.MonthCloses.OrderBy(m => m.Month).ToListAsync();
        Assert.Equal([7, 8], closes.Select(c => c.Month).ToArray());
        Assert.All(closes, c => Assert.Equal(goal.Id, c.TransferredGoalId));

        var transfers = await t.Context.GoalContributions.Where(c => c.Source == GoalContributionSource.MonthClose).ToListAsync();
        Assert.Equal(closes.Sum(c => c.Remaining), transfers.Sum(c => c.Amount));
        Assert.Single(t.Context.GoalContributions, c => c.Source == GoalContributionSource.Manual);
    }

    [Fact]
    public async Task SeedDataAsync_StoredCloseAmountsMatchTheRealMonthlyBalance()
    {
        using var t = await SeededDb();
        var service = new MonthCloseService(t.Context);

        foreach (var close in await t.Context.MonthCloses.ToListAsync())
            Assert.Equal(await service.CalculateRemainingAsync(Demo, close.Year, close.Month), close.Remaining);

        Assert.Equal(891.02m, await service.CalculateRemainingAsync(Demo, 2026, 7));
        Assert.Equal(391.02m, await service.CalculateRemainingAsync(Demo, 2026, 8));
        Assert.Equal(291.02m, await service.CalculateRemainingAsync(Demo, 2026, 9));
    }

    [Fact]
    public async Task SeedDataAsync_LeavesLastMonthOpenSoTheHomePageShowsTheTransferCard()
    {
        using var t = await SeededDb();

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Demo, Today);

        Assert.NotNull(pending);
        Assert.Equal(9, pending.Month);
        Assert.Equal(291.02m, pending.Remaining);
        Assert.NotNull(pending.TargetGoal);
    }

    [Fact]
    public async Task SeedDataAsync_TheOpenMonthAlreadyHasAManualAddSoOnlyTheRestIsOffered()
    {
        using var t = await SeededDb();

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Demo, Today);

        Assert.Equal(291.02m, pending!.Remaining);
        Assert.Equal(150m, pending.AlreadyAdded);
        Assert.Equal(141.02m, pending.Transferable);
    }

    [Fact]
    public async Task SeedDataAsync_ClosedMonthsDidNotTransferMoneyThatWasAddedByHandInThatMonth()
    {
        using var t = await SeededDb();

        // No manual contribution falls into a month that was closed with a transfer.
        foreach (var close in await t.Context.MonthCloses.ToListAsync())
        {
            var start = new DateTime(close.Year, close.Month, 1);
            var manualInMonth = t.Context.GoalContributions.AsEnumerable()
                .Where(c => c.Source == GoalContributionSource.Manual && c.Date >= start && c.Date < start.AddMonths(1))
                .Sum(c => c.Amount);
            Assert.Equal(0m, manualInMonth);
        }
    }

    // ---------- recurring definitions ----------

    [Fact]
    public async Task SeedDataAsync_CreatesRecurringDefinitionsAndLinksTheirMonthlyTransactions()
    {
        using var t = await SeededDb(); // 15 October

        var definitions = await t.Context.RecurringTransactions.Include(r => r.Category).ToListAsync();
        Assert.Equal(8, definitions.Count);
        Assert.All(definitions, d =>
        {
            Assert.Equal(Demo, d.UserId);
            Assert.True(d.IsActive);
        });
        foreach (var category in new[] { "Aile Desteği", "KYK Bursu", "Part-time İş", "Kira/Yurt", "Faturalar", "Abonelikler" })
            Assert.Contains(definitions, d => d.Category!.Name == category);

        // Three completed months each, plus October for everything whose day has already come (all but the part-time pay).
        var linked = await t.Context.Transactions.Where(x => x.RecurringTransactionId != null).ToListAsync();
        foreach (var d in definitions)
        {
            var expected = d.Category!.Name == "Part-time İş" ? 3 : 4;
            Assert.Equal(expected, linked.Count(x => x.RecurringTransactionId == d.Id));
        }
        Assert.All(linked, x => Assert.Equal(new DateTime(x.Date.Year, x.Date.Month, 1), x.RecurringPeriod));
    }

    [Theory]
    [InlineData(2026, 10, 3)]
    [InlineData(2026, 10, 8)]
    [InlineData(2026, 10, 15)]
    [InlineData(2026, 10, 28)]
    [InlineData(2026, 11, 1)]
    [InlineData(2027, 1, 2)]
    public async Task SeedDataAsync_LeavesNothingForGenerationToDuplicate(int year, int month, int day)
    {
        var today = new DateTime(year, month, day);
        using var t = await SeededDb(today);
        var before = t.Context.Transactions.Count();

        var created = await new RecurringTransactionService(t.Context).GenerateDueAsync(Demo, today);

        Assert.Empty(created);
        Assert.Equal(before, t.Context.Transactions.Count());
    }

    [Fact]
    public async Task SeedDataAsync_TheRestOfTheMonthArrivesOnItsOwn()
    {
        using var t = await SeededDb(); // 15 October: only the part-time pay of the 20th is still to come
        var service = new RecurringTransactionService(t.Context);

        var pending = Assert.Single(await service.GetPendingAsync(Demo, Today));
        Assert.Equal(new DateTime(2026, 10, 20), pending.DueDate);
        Assert.Equal(1500m, pending.Definition.Amount);
        Assert.Equal(1500m, (await new ForecastService(t.Context).GetProjectedBalanceAsync(Demo, Today))!.PendingIncome);

        var onThe20th = Assert.Single(await service.GenerateDueAsync(Demo, new DateTime(2026, 10, 20)));
        Assert.Equal((TransactionType.Income, 1500m), (onThe20th.Type, onThe20th.Amount));

        var firstOfNovember = await service.GenerateDueAsync(Demo, new DateTime(2026, 11, 1));
        Assert.Equal([3000m, 3500m], firstOfNovember.Select(x => x.Amount).OrderBy(a => a).ToArray()); // rent and family support
    }

    // ---------- the demo tells a story ----------

    [Fact]
    public async Task Forecast_HasAMonthEndProjectionAndAGoalEstimate()
    {
        using var t = await SeededDb();
        var forecast = new ForecastService(t.Context);

        Assert.NotNull(await forecast.GetProjectedBalanceAsync(Demo, Today));

        var goal = await forecast.GetGoalForecastAsync(Demo, Today);
        Assert.NotNull(goal);
        Assert.Equal(GoalForecastStatus.Reachable, goal.Status);
        Assert.Equal(6, goal.Months); // 3.067,96 TL missing at about 524 TL saved per month
        Assert.Equal(new DateTime(2027, 4, 1), goal.ReachDate);
    }

    [Fact]
    public async Task Analysis_OfTheLastCompleteMonth_GivesMeaningfulAdvice()
    {
        using var t = await SeededDb();
        var analysis = new AnalysisService(t.Context, new ForecastService(t.Context));

        var result = await analysis.GetMonthlyAnalysisAsync(Demo, 2026, 9, Today);

        Assert.Equal("Latte", result.TopItems[0].ItemName);
        Assert.DoesNotContain(result.TopItems, i => i.ItemName is "Yurt ücreti" or "Elektrik faturası" or "Netflix");
        Assert.InRange(result.Advice.Count, 2, 3);
        Assert.Contains("Kafe/Kahve", result.Advice[0]);
        Assert.Contains("ay erken", result.Advice[0]); // halving coffee brings the goal closer
        Assert.Contains(result.Advice, a => a.Contains("Latte için bu ay 11 kez toplam 990,00"));
        Assert.True(result.Changes.Single(c => c.Name == "Kafe/Kahve").Delta > 0);
    }

    [Fact]
    public async Task Analysis_OfTheCurrentMonth_HasCategoriesAndTips()
    {
        using var t = await SeededDb();
        var analysis = new AnalysisService(t.Context, new ForecastService(t.Context));

        var result = await analysis.GetMonthlyAnalysisAsync(Demo, 2026, 10, Today);

        Assert.NotEmpty(result.Categories);
        Assert.NotEmpty(result.TopItems);
        Assert.InRange(result.Advice.Count, 2, 3);
    }

    [Fact]
    public async Task Warnings_AllFourRulesFireWhenTheDemoUserOverspendsMidMonth()
    {
        using var t = await SeededDb();
        var entertainment = await t.Context.Categories.SingleAsync(c => c.Name == "Eğlence");
        var saved = TestData.Add(t.Context, Demo, TransactionType.Expense, 600m, Today, entertainment.Id, "Konser bileti");

        var warnings = await new WarningService(t.Context, new ForecastService(t.Context)).CheckAsync(Demo, saved, Today);

        Assert.Equal(
            [WarningRule.LargeExpense, WarningRule.UnnecessaryShare, WarningRule.CategoryIncrease, WarningRule.NegativeForecast],
            warnings.Select(w => w.Rule).ToArray());
        Assert.All(warnings, w => Assert.Contains(",", w.Message)); // every message carries real, formatted numbers
    }

    [Theory]
    [InlineData(8)]
    [InlineData(28)]
    public async Task Warnings_AnExtraBigExpenseIsFlaggedOnOtherDaysToo(int day)
    {
        var today = new DateTime(2026, 10, day);
        using var t = await SeededDb(today);
        var entertainment = await t.Context.Categories.SingleAsync(c => c.Name == "Eğlence");
        var saved = TestData.Add(t.Context, Demo, TransactionType.Expense, 600m, today, entertainment.Id, "Konser bileti");

        var warnings = await new WarningService(t.Context, new ForecastService(t.Context)).CheckAsync(Demo, saved, today);

        Assert.NotEmpty(warnings);
    }
}
