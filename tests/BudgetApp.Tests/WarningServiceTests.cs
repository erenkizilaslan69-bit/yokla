using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class WarningServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Today = new(2026, 10, 10);

    private static WarningService NewService(TestDb t) => new(t.Context, new ForecastService(t.Context));

    private static async Task<List<Warning>> Check(TestDb t, Transaction saved, DateTime? today = null) =>
        await NewService(t).CheckAsync(Alice, saved, today ?? Today);

    private static bool Has(List<Warning> warnings, WarningRule rule) => warnings.Any(w => w.Rule == rule);

    /// <summary>Income so the forecast and share rules have a baseline and stay quiet unless a test wants them.</summary>
    private static void AddSalary(TestDb t, decimal amount = 100_000m) =>
        AddIncome(t.Context, Alice, amount, new DateTime(2026, 10, 1));

    // ---------- scope ----------

    [Fact]
    public async Task CheckAsync_ReturnsNothingForIncomeTransactions()
    {
        using var t = new TestDb();
        var income = AddIncome(t.Context, Alice, 5000m, new DateTime(2026, 10, 10));

        Assert.Empty(await Check(t, income));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNothingForTransactionsOutsideTheCurrentMonth()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 5));
        var old = AddExpense(t.Context, Alice, 9000m, new DateTime(2026, 9, 6), EatingOut);

        Assert.Empty(await Check(t, old));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNothingWhenEverythingIsFine()
    {
        using var t = new TestDb();
        AddSalary(t);
        var saved = AddExpense(t.Context, Alice, 50m, new DateTime(2026, 10, 10));

        Assert.Empty(await Check(t, saved));
    }

    // ---------- rule 1: large expense ----------

    private static void SeedDailyAverage(TestDb t, decimal total = 900m)
    {
        // 900 over Oct 1-9 variable spending; with today = Oct 10 the average is 90 per day.
        AddExpense(t.Context, Alice, total, new DateTime(2026, 10, 5));
    }

    [Fact]
    public async Task Rule1_FiresWhenAmountExceedsThreeTimesDailyAverage()
    {
        using var t = new TestDb();
        AddSalary(t);
        SeedDailyAverage(t); // average 90 => limit 270
        var saved = AddExpense(t.Context, Alice, 300m, new DateTime(2026, 10, 10));

        var warnings = await Check(t, saved);

        var w = Assert.Single(warnings, x => x.Rule == WarningRule.LargeExpense);
        Assert.Contains("300,00", w.Message);
    }

    [Fact]
    public async Task Rule1_DoesNotFireAtExactlyThreeTimesTheAverage()
    {
        using var t = new TestDb();
        AddSalary(t);
        SeedDailyAverage(t);
        var saved = AddExpense(t.Context, Alice, 270m, new DateTime(2026, 10, 10));

        Assert.False(Has(await Check(t, saved), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_DoesNotCountTheSavedExpenseInTheAverage()
    {
        using var t = new TestDb();
        AddSalary(t);
        SeedDailyAverage(t);
        // If the saved 280 were included the average would be 118 and the limit 354 (no warning). Excluded: limit 270.
        var saved = AddExpense(t.Context, Alice, 280m, new DateTime(2026, 10, 10));

        Assert.True(Has(await Check(t, saved), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_DoesNotFireInTheFirstThreeDaysOfTheMonth()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 30m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 5000m, new DateTime(2026, 10, 3));

        Assert.False(Has(await Check(t, saved, new DateTime(2026, 10, 3)), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_FiresFromTheFourthDay()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 40m, new DateTime(2026, 10, 1)); // average 10 per day on Oct 4
        var saved = AddExpense(t.Context, Alice, 500m, new DateTime(2026, 10, 4));

        Assert.True(Has(await Check(t, saved, new DateTime(2026, 10, 4)), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_DoesNotFireWithoutAnyOtherVariableSpending()
    {
        using var t = new TestDb();
        AddSalary(t);
        var saved = AddExpense(t.Context, Alice, 5000m, new DateTime(2026, 10, 10));

        Assert.False(Has(await Check(t, saved), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_DoesNotFireForFixedExpenseCategories()
    {
        using var t = new TestDb();
        AddSalary(t);
        SeedDailyAverage(t);
        var rent = AddExpense(t.Context, Alice, 3000m, new DateTime(2026, 10, 10), Rent);

        Assert.False(Has(await Check(t, rent), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_AverageIgnoresFixedExpenses()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 2000m, new DateTime(2026, 10, 1), Rent); // would inflate the average if counted
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 10, 5));        // variable average 10 => limit 30
        var saved = AddExpense(t.Context, Alice, 40m, new DateTime(2026, 10, 10));

        Assert.True(Has(await Check(t, saved), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_IgnoresOtherUsersSpending()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Bob, 90_000m, new DateTime(2026, 10, 5)); // Bob's spending must not raise Alice's average
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 10, 5));
        var saved = AddExpense(t.Context, Alice, 40m, new DateTime(2026, 10, 10));

        Assert.True(Has(await Check(t, saved), WarningRule.LargeExpense));
    }

    // ---------- rule 2: unnecessary share ----------

    [Fact]
    public async Task Rule2_FiresWhenUnnecessaryTotalExceedsTwentyPercentOfIncome()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 201m, new DateTime(2026, 10, 10), EatingOut);

        var warnings = await Check(t, saved);

        var w = Assert.Single(warnings, x => x.Rule == WarningRule.UnnecessaryShare);
        Assert.Contains("201,00", w.Message);
    }

    [Fact]
    public async Task Rule2_DoesNotFireAtExactlyTwentyPercent()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 200m, new DateTime(2026, 10, 10), EatingOut);

        Assert.False(Has(await Check(t, saved), WarningRule.UnnecessaryShare));
    }

    [Fact]
    public async Task Rule2_SumsAllUnnecessaryCategories()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 150m, new DateTime(2026, 10, 4), EatingOut);
        AddExpense(t.Context, Alice, 40m, new DateTime(2026, 10, 6), Entertainment);
        var saved = AddExpense(t.Context, Alice, 20m, new DateTime(2026, 10, 10), Coffee); // 210 > 200

        Assert.True(Has(await Check(t, saved), WarningRule.UnnecessaryShare));
    }

    [Fact]
    public async Task Rule2_DoesNotFireWhenSavedExpenseIsNotUnnecessary()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 500m, new DateTime(2026, 10, 4), EatingOut);
        var saved = AddExpense(t.Context, Alice, 10m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.UnnecessaryShare));
    }

    [Fact]
    public async Task Rule2_DoesNotFireWithoutIncome()
    {
        using var t = new TestDb();
        var saved = AddExpense(t.Context, Alice, 100m, new DateTime(2026, 10, 10), EatingOut);

        Assert.False(Has(await Check(t, saved), WarningRule.UnnecessaryShare));
    }

    [Fact]
    public async Task Rule2_IgnoresPreviousMonthAndOtherUsers()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 900m, new DateTime(2026, 9, 20), EatingOut);
        AddExpense(t.Context, Bob, 900m, new DateTime(2026, 10, 5), EatingOut);
        var saved = AddExpense(t.Context, Alice, 100m, new DateTime(2026, 10, 10), EatingOut);

        Assert.False(Has(await Check(t, saved), WarningRule.UnnecessaryShare));
    }

    // ---------- rule 3: category increase ----------

    [Fact]
    public async Task Rule3_FiresWhenCategoryIsMoreThan130PercentOfLastMonthSameDay()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 8), Market);   // up to Sept 10: 100
        AddExpense(t.Context, Alice, 500m, new DateTime(2026, 9, 20), Market);  // after the cutoff: ignored
        var saved = AddExpense(t.Context, Alice, 131m, new DateTime(2026, 10, 10), Market); // 131 > 130

        var warnings = await Check(t, saved);

        var w = Assert.Single(warnings, x => x.Rule == WarningRule.CategoryIncrease);
        Assert.Contains("Market", w.Message);
        Assert.Contains("131,00", w.Message);
    }

    [Fact]
    public async Task Rule3_DoesNotFireAtExactly130Percent()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 8), Market);
        var saved = AddExpense(t.Context, Alice, 130m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.CategoryIncrease));
    }

    [Fact]
    public async Task Rule3_DoesNotFireWhenLastMonthHadNoSpendingInTheCategory()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 900m, new DateTime(2026, 9, 8), Transport); // different category
        var saved = AddExpense(t.Context, Alice, 5000m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.CategoryIncrease));
    }

    [Fact]
    public async Task Rule3_DoesNotFireWhenLastMonthSpendingCameOnlyAfterTheSameDay()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 900m, new DateTime(2026, 9, 25), Market); // after Sept 10
        var saved = AddExpense(t.Context, Alice, 50m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.CategoryIncrease));
    }

    [Fact]
    public async Task Rule3_UsesLastDayOfShorterPreviousMonth()
    {
        using var t = new TestDb();
        AddSalary(t);
        // Today is Oct 31, September has 30 days: the comparison covers all of September.
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 30), Market);
        var saved = AddExpense(t.Context, Alice, 131m, new DateTime(2026, 10, 31), Market);

        Assert.True(Has(await Check(t, saved, new DateTime(2026, 10, 31)), WarningRule.CategoryIncrease));
    }

    [Fact]
    public async Task Rule3_OnlyLooksAtTheSavedExpensesCategory()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 8), Market);
        AddExpense(t.Context, Alice, 500m, new DateTime(2026, 10, 5), Market); // Market is way up...
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 8), Transport);
        var saved = AddExpense(t.Context, Alice, 50m, new DateTime(2026, 10, 10), Transport); // ...but Transport is fine

        Assert.False(Has(await Check(t, saved), WarningRule.CategoryIncrease));
    }

    [Fact]
    public async Task Rule3_IgnoresOtherUsers()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddExpense(t.Context, Bob, 10m, new DateTime(2026, 9, 8), Market);
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 9, 8), Market);
        AddExpense(t.Context, Bob, 9000m, new DateTime(2026, 10, 5), Market);
        var saved = AddExpense(t.Context, Alice, 50m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.CategoryIncrease));
    }

    // ---------- rule 4: negative forecast ----------

    [Fact]
    public async Task Rule4_FiresWhenProjectedMonthEndBalanceIsNegative()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market); // 1000 - 400*31/10 = -240

        var warnings = await Check(t, saved);

        var w = Assert.Single(warnings, x => x.Rule == WarningRule.NegativeForecast);
        Assert.Contains("240,00", w.Message);
    }

    [Fact]
    public async Task Rule4_DoesNotFireWhenProjectionIsPositive()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 4000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 2200m, new DateTime(2026, 10, 1), Rent);
        var saved = AddExpense(t.Context, Alice, 250m, new DateTime(2026, 10, 7), Market); // ~693 left

        Assert.False(Has(await Check(t, saved, new DateTime(2026, 10, 7)), WarningRule.NegativeForecast));
    }

    [Fact]
    public async Task Rule4_UsesTheFixedVariableForecast()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 4000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 2200m, new DateTime(2026, 10, 1), Rent);
        // Projection: 4000 - 2200 - 400 * 31 / 7 = about +28.57. If the fixed rent were scaled too,
        // the result would be a deep deficit, so a warning here would mean the formula is wrong.
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 7), Market);

        Assert.False(Has(await Check(t, saved, new DateTime(2026, 10, 7)), WarningRule.NegativeForecast));
    }

    [Fact]
    public async Task Rule4_DoesNotFireWhenThereIsNoIncomeThisMonth()
    {
        using var t = new TestDb();
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.NegativeForecast));
    }

    [Fact]
    public async Task Rule4_IgnoresIncomeFromOtherMonthsAndOtherUsers()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 9000m, new DateTime(2026, 9, 1)); // last month's income does not count
        AddIncome(t.Context, Bob, 9000m, new DateTime(2026, 10, 1));  // neither does somebody else's
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market);

        Assert.False(Has(await Check(t, saved), WarningRule.NegativeForecast));
    }

    [Fact]
    public async Task Rule4_FiresAsSoonAsAnIncomeIsRecordedThisMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 100m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market);

        Assert.True(Has(await Check(t, saved), WarningRule.NegativeForecast));
    }

    [Fact]
    public async Task Rule4_FiresForFixedExpensesThatAlreadyExceedIncome()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 1500m, new DateTime(2026, 10, 2), Rent);

        Assert.True(Has(await Check(t, saved, new DateTime(2026, 10, 2)), WarningRule.NegativeForecast));
    }

    // ---------- rule 5: category budget ----------

    private static Warning? BudgetWarning(List<Warning> warnings) =>
        warnings.SingleOrDefault(w => w.Rule == WarningRule.CategoryBudget);

    [Fact]
    public async Task Rule5_SaysYouAreApproachingAbove80PercentOfTheLimit()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var saved = AddExpense(t.Context, Alice, 401m, new DateTime(2026, 10, 10), Coffee);

        var warning = BudgetWarning(await Check(t, saved));

        Assert.NotNull(warning);
        Assert.Contains("Kafe/Kahve", warning.Message);
        Assert.Contains("%80", warning.Message);
        Assert.Contains("₺401,00 / ₺500,00", warning.Message);
        Assert.Contains("₺99,00 kaldı", warning.Message);
    }

    [Fact]
    public async Task Rule5_ExactlyEightyPercentIsStillQuiet()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Coffee);

        Assert.Null(BudgetWarning(await Check(t, saved)));
    }

    [Fact]
    public async Task Rule5_ExactlyTheLimitIsStillApproachingNotExceeded()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var saved = AddExpense(t.Context, Alice, 500m, new DateTime(2026, 10, 10), Coffee);

        var warning = BudgetWarning(await Check(t, saved));

        Assert.Contains("%100", warning!.Message);
        Assert.Contains("kullandın", warning.Message);
        Assert.DoesNotContain("aştın", warning.Message);
    }

    [Fact]
    public async Task Rule5_JustBelowTheLimitDoesNotReadAsOneHundredPercent()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 1000m);
        var saved = AddExpense(t.Context, Alice, 999m, new DateTime(2026, 10, 10), Coffee);

        Assert.Contains("%99", BudgetWarning(await Check(t, saved))!.Message);
    }

    [Fact]
    public async Task Rule5_SaysYouExceededTheLimitAbove100PercentWithTheAmountOver()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var saved = AddExpense(t.Context, Alice, 540m, new DateTime(2026, 10, 10), Coffee);

        var warning = BudgetWarning(await Check(t, saved));

        Assert.Contains("aştın", warning!.Message);
        Assert.Contains("₺540,00 / ₺500,00", warning.Message);
        Assert.Contains("₺40,00 fazla", warning.Message);
    }

    [Fact]
    public async Task Rule5_EachLevelIsReportedOnlyOncePerMonth()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var date = new DateTime(2026, 10, 10);

        var first = AddExpense(t.Context, Alice, 410m, date, Coffee);      // 82%: approaching
        Assert.Contains("kullandın", BudgetWarning(await Check(t, first))!.Message);

        var second = AddExpense(t.Context, Alice, 20m, date, Coffee);      // 86%: already told
        Assert.Null(BudgetWarning(await Check(t, second)));

        var third = AddExpense(t.Context, Alice, 100m, date, Coffee);      // 106%: now exceeded
        Assert.Contains("aştın", BudgetWarning(await Check(t, third))!.Message);

        var fourth = AddExpense(t.Context, Alice, 50m, date, Coffee);      // still exceeded: no repeat
        Assert.Null(BudgetWarning(await Check(t, fourth)));
        var fifth = AddExpense(t.Context, Alice, 5m, date, Coffee);
        Assert.Null(BudgetWarning(await Check(t, fifth)));
    }

    [Fact]
    public async Task Rule5_JumpingStraightOverTheLimitGivesOnlyTheExceededWarning()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var big = AddExpense(t.Context, Alice, 600m, new DateTime(2026, 10, 10), Coffee);

        Assert.Contains("aştın", BudgetWarning(await Check(t, big))!.Message);

        var next = AddExpense(t.Context, Alice, 10m, new DateTime(2026, 10, 10), Coffee);
        Assert.Null(BudgetWarning(await Check(t, next)));
    }

    [Fact]
    public async Task Rule5_RemembersWhatWasReported()
    {
        using var t = new TestDb();
        AddSalary(t);
        var budget = AddBudget(t.Context, Alice, Coffee, 500m);
        var saved = AddExpense(t.Context, Alice, 410m, new DateTime(2026, 10, 10), Coffee);

        await Check(t, saved);

        var stored = t.Context.CategoryBudgets.AsNoTracking().Single(b => b.Id == budget.Id);
        Assert.Equal(BudgetLevel.Approaching, stored.WarnedLevel);
        Assert.Equal(new DateTime(2026, 10, 1), stored.WarnedPeriod);
    }

    [Fact]
    public async Task Rule5_AWarningFromAnEarlierMonthDoesNotSilenceThisMonth()
    {
        using var t = new TestDb();
        AddSalary(t);
        var budget = AddBudget(t.Context, Alice, Coffee, 500m);
        budget.WarnedPeriod = new DateTime(2026, 9, 1);
        budget.WarnedLevel = BudgetLevel.Exceeded;
        t.Context.SaveChanges();
        var saved = AddExpense(t.Context, Alice, 450m, new DateTime(2026, 10, 10), Coffee);

        var warning = BudgetWarning(await Check(t, saved));

        Assert.Contains("kullandın", warning!.Message);
    }

    [Fact]
    public async Task Rule5_ChangingTheLimitMakesTheWarningsPossibleAgain()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        var saved = AddExpense(t.Context, Alice, 540m, new DateTime(2026, 10, 10), Coffee);
        Assert.Contains("aştın", BudgetWarning(await Check(t, saved))!.Message);

        await new BudgetService(t.Context).SetAsync(Alice, Coffee, 600m);     // 540 / 600 = 90%
        var next = AddExpense(t.Context, Alice, 10m, new DateTime(2026, 10, 10), Coffee);

        Assert.Contains("kullandın", BudgetWarning(await Check(t, next))!.Message);
    }

    [Fact]
    public async Task Rule5_OnlyLooksAtTheSavedExpensesCategoryAndUser()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 100m);
        AddBudget(t.Context, Bob, Market, 10m);
        AddExpense(t.Context, Alice, 5000m, new DateTime(2026, 10, 3), Coffee);          // Coffee is far over, but...
        var saved = AddExpense(t.Context, Alice, 50m, new DateTime(2026, 10, 10), Market); // ...this is Market (no limit for Alice)

        Assert.Null(BudgetWarning(await Check(t, saved)));
    }

    [Fact]
    public async Task Rule5_NeedsALimitForTheCategory()
    {
        using var t = new TestDb();
        AddSalary(t);
        var saved = AddExpense(t.Context, Alice, 5000m, new DateTime(2026, 10, 10), Coffee);

        Assert.Null(BudgetWarning(await Check(t, saved)));
    }

    [Fact]
    public async Task Rule5_CountsRecurringExpensesInTheCategoryTotal()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Bills, 500m);
        AddGeneratedExpense(t.Context, Alice, 450m, new DateTime(2026, 10, 1), Bills, "İnternet"); // generated: no warning of its own
        var saved = AddExpense(t.Context, Alice, 60m, new DateTime(2026, 10, 10), Bills, "Su");     // 510 in total

        Assert.Contains("aştın", BudgetWarning(await Check(t, saved))!.Message);
    }

    [Fact]
    public async Task Rule5_IgnoresSpendingOfOtherMonths()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddBudget(t.Context, Alice, Coffee, 500m);
        AddExpense(t.Context, Alice, 450m, new DateTime(2026, 9, 28), Coffee);
        var saved = AddExpense(t.Context, Alice, 100m, new DateTime(2026, 10, 10), Coffee);

        Assert.Null(BudgetWarning(await Check(t, saved)));
    }

    [Fact]
    public async Task Rule5_AnOldExpenseDoesNotWarnOrChangeTheWarningState()
    {
        using var t = new TestDb();
        AddSalary(t);
        var budget = AddBudget(t.Context, Alice, Coffee, 500m);
        var old = AddExpense(t.Context, Alice, 900m, new DateTime(2026, 9, 5), Coffee);

        Assert.Empty(await Check(t, old));
        var stored = t.Context.CategoryBudgets.AsNoTracking().Single(b => b.Id == budget.Id);
        Assert.Equal(BudgetLevel.None, stored.WarnedLevel);
    }

    [Fact]
    public async Task Rule5_SitsAfterTheOtherRulesInTheWarningList()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddBudget(t.Context, Alice, EatingOut, 300m);
        AddExpense(t.Context, Alice, 30m, new DateTime(2026, 9, 8), EatingOut);
        AddExpense(t.Context, Alice, 90m, new DateTime(2026, 10, 5), EatingOut);
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), EatingOut);

        var rules = (await Check(t, saved)).Select(w => w.Rule).ToList();

        Assert.Equal(
            [WarningRule.LargeExpense, WarningRule.UnnecessaryShare, WarningRule.CategoryIncrease,
             WarningRule.NegativeForecast, WarningRule.CategoryBudget],
            rules);
    }

    // ---------- recurring definitions ----------

    [Fact]
    public async Task Rule4_DoesNotFireWhenUpcomingRecurringIncomeCoversTheSpending()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market); // alone: 1000 - 1240 = -240
        Assert.True(Has(await Check(t, saved), WarningRule.NegativeForecast));

        // The part-time pay of the 20th is known in advance.
        AddRecurring(t.Context, Alice, TransactionType.Income, 1500m, day: 20, start: new DateTime(2026, 10, 1));

        Assert.False(Has(await Check(t, saved), WarningRule.NegativeForecast));
    }

    [Fact]
    public async Task Rule4_FiresWhenOnlyUpcomingRecurringIncomeIsKnownAndItIsNotEnough()
    {
        using var t = new TestDb();
        AddRecurring(t.Context, Alice, TransactionType.Income, 1000m, day: 20, start: new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market); // 1000 - 400 * 31 / 10 = -240

        var warnings = await Check(t, saved);

        var w = Assert.Single(warnings, x => x.Rule == WarningRule.NegativeForecast);
        Assert.Contains("240,00", w.Message);
    }

    [Fact]
    public async Task Rule4_CountsUpcomingRecurringExpenses()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1500m, new DateTime(2026, 10, 1));
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), Market); // alone: 1500 - 1240 = +260
        Assert.False(Has(await Check(t, saved), WarningRule.NegativeForecast));

        AddRecurring(t.Context, Alice, TransactionType.Expense, 300m, day: 25, start: new DateTime(2026, 10, 1), categoryId: Bills);

        Assert.True(Has(await Check(t, saved), WarningRule.NegativeForecast)); // 260 - 300 = -40
    }

    [Fact]
    public async Task Rule1_AverageIgnoresExpensesGeneratedFromRecurringDefinitions()
    {
        using var t = new TestDb();
        AddSalary(t);
        AddGeneratedExpense(t.Context, Alice, 2000m, new DateTime(2026, 10, 1), Transport); // would inflate the average if counted
        AddExpense(t.Context, Alice, 100m, new DateTime(2026, 10, 5));                      // variable average 10 => limit 30
        var saved = AddExpense(t.Context, Alice, 40m, new DateTime(2026, 10, 10));

        Assert.True(Has(await Check(t, saved), WarningRule.LargeExpense));
    }

    [Fact]
    public async Task Rule1_DoesNotFireForARecurringOccurrence()
    {
        using var t = new TestDb();
        AddSalary(t);
        SeedDailyAverage(t); // average 90 => limit 270
        var monthlyPass = AddGeneratedExpense(t.Context, Alice, 3000m, new DateTime(2026, 10, 10), Transport);

        Assert.False(Has(await Check(t, monthlyPass), WarningRule.LargeExpense));
    }

    // ---------- several rules and ordering ----------

    [Fact]
    public async Task CheckAsync_ReportsSeveralRulesInRuleOrder()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1));
        AddExpense(t.Context, Alice, 30m, new DateTime(2026, 9, 8), EatingOut);
        AddExpense(t.Context, Alice, 90m, new DateTime(2026, 10, 5), EatingOut);
        var saved = AddExpense(t.Context, Alice, 400m, new DateTime(2026, 10, 10), EatingOut);

        var rules = (await Check(t, saved)).Select(w => w.Rule).ToList();

        Assert.Equal([WarningRule.LargeExpense, WarningRule.UnnecessaryShare, WarningRule.CategoryIncrease, WarningRule.NegativeForecast], rules);
    }

    [Fact]
    public async Task CheckManyAsync_GivesEachMessageOnceInTheOrderTheyCameUp()
    {
        using var t = new TestDb();
        AddSalary(t, 1_000m);
        var first = AddExpense(t.Context, Alice, 900m, Today, Coffee);
        var second = AddExpense(t.Context, Alice, 900m, Today, Coffee);
        var income = AddIncome(t.Context, Alice, 50m, Today);

        var messages = await NewService(t).CheckManyAsync(Alice, [first, second, income], Today);
        var one = (await NewService(t).CheckAsync(Alice, first, Today)).Select(w => w.Message).ToList();
        var two = (await NewService(t).CheckAsync(Alice, second, Today)).Select(w => w.Message).ToList();

        Assert.Equal(one.Concat(two).Distinct(), messages);
        Assert.True(one.Intersect(two).Any()); // the two lines do share a warning, which is shown once
    }

    [Fact]
    public async Task CheckManyAsync_NothingSavedNothingToSay()
    {
        using var t = new TestDb();
        Assert.Empty(await NewService(t).CheckManyAsync(Alice, [], Today));
    }
}
