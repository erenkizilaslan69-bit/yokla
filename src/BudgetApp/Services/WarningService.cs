using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public enum WarningRule
{
    LargeExpense = 1,
    UnnecessaryShare = 2,
    CategoryIncrease = 3,
    NegativeForecast = 4,
    /// <summary>The category's monthly limit is nearly used up or exceeded (either message is rule 5).</summary>
    CategoryBudget = 5
}

public record Warning(WarningRule Rule, string Message);

public interface IWarningService
{
    /// <summary>Evaluates the warning rules for an expense that was just saved (it must already be persisted).</summary>
    Task<List<Warning>> CheckAsync(string userId, Transaction saved, DateTime today);
}

public class WarningService(ApplicationDbContext db, IForecastService forecast) : IWarningService
{
    private const int LargeExpenseMinDay = 4;          // rule 1 is skipped on days 1-3
    private const decimal LargeExpenseFactor = 3m;
    private const decimal UnnecessaryShareLimit = 0.20m;
    private const decimal CategoryIncreaseLimit = 1.30m;

    public async Task<List<Warning>> CheckAsync(string userId, Transaction saved, DateTime today)
    {
        var warnings = new List<Warning>();
        if (saved.Type != TransactionType.Expense || saved.Date.Year != today.Year || saved.Date.Month != today.Month)
            return warnings;

        var rows = await MonthData.GetMonthAsync(db, userId, today.Year, today.Month);
        var savedRow = rows.FirstOrDefault(r => r.Id == saved.Id);
        if (savedRow is null)
            return warnings;

        AddIfNotNull(warnings, LargeExpense(rows, savedRow, today));
        AddIfNotNull(warnings, UnnecessaryShare(rows, savedRow));
        AddIfNotNull(warnings, await CategoryIncrease(userId, rows, savedRow, today));
        AddIfNotNull(warnings, await NegativeForecast(userId, today));
        AddIfNotNull(warnings, await CategoryBudgetLimit(userId, rows, savedRow, today));
        return warnings;
    }

    // Rule 5: the category's total this month is above 80% (approaching) or above 100% (exceeded) of its monthly limit.
    // Unlike rules 1-4 this one is remembered: each level is reported once per month, so the budget row is updated.
    private async Task<Warning?> CategoryBudgetLimit(string userId, List<MonthRow> rows, MonthRow saved, DateTime today)
    {
        var budget = await db.CategoryBudgets.FirstOrDefaultAsync(b => b.UserId == userId && b.CategoryId == saved.CategoryId);
        if (budget is null)
            return null;

        var spent = rows.Where(r => r.IsExpense && r.CategoryId == saved.CategoryId).Sum(r => r.Amount);
        var level = BudgetMath.LevelFor(spent, budget.MonthlyLimit);
        if (level == BudgetLevel.None)
            return null;

        var period = new DateTime(today.Year, today.Month, 1);
        var alreadyWarned = budget.WarnedPeriod == period ? budget.WarnedLevel : BudgetLevel.None;
        if (level <= alreadyWarned)
            return null;

        budget.WarnedPeriod = period;
        budget.WarnedLevel = level;
        await db.SaveChangesAsync();

        var limit = budget.MonthlyLimit;
        if (level == BudgetLevel.Exceeded)
            return new Warning(WarningRule.CategoryBudget,
                $"{saved.CategoryName} bütçeni aştın: {Tr.Money(spent)} / {Tr.Money(limit)} ({Tr.Money(spent - limit)} fazla).");

        var usedPercent = (int)Math.Floor(spent / limit * 100m); // rounding down: 99,6% must not read as "%100"
        return new Warning(WarningRule.CategoryBudget,
            $"{saved.CategoryName} bütçenin %{usedPercent}'ini kullandın: {Tr.Money(spent)} / {Tr.Money(limit)}. " +
            $"Ay bitmeden {Tr.Money(limit - spent)} kaldı.");
    }

    private static void AddIfNotNull(List<Warning> list, Warning? warning)
    {
        if (warning is not null)
            list.Add(warning);
    }

    // Rule 1: a single variable expense above three times the month's daily average (saved expense excluded).
    // Fixed categories and recurring occurrences are planned once-a-month amounts: they neither trigger the rule
    // nor count towards the average.
    private static Warning? LargeExpense(List<MonthRow> rows, MonthRow saved, DateTime today)
    {
        if (saved.IsFixedLike || today.Day < LargeExpenseMinDay)
            return null;

        var others = rows.Where(r => r.IsExpense && !r.IsFixedLike && r.Id != saved.Id).Sum(r => r.Amount);
        var dailyAverage = others / today.Day;
        if (dailyAverage <= 0 || saved.Amount <= LargeExpenseFactor * dailyAverage)
            return null;

        return new Warning(WarningRule.LargeExpense,
            $"{saved.CategoryName} için tek seferde {Tr.Money(saved.Amount)} harcadın; bu ayki günlük ortalaman " +
            $"{Tr.Money(dailyAverage)}. Bu, ortalamanın üç katından fazla!");
    }

    // Rule 2: "unnecessary" categories took more than 20% of this month's income.
    private static Warning? UnnecessaryShare(List<MonthRow> rows, MonthRow saved)
    {
        if (!saved.IsUnnecessary)
            return null;

        var income = MonthData.Income(rows);
        if (income <= 0)
            return null;

        var total = rows.Where(r => r.IsExpense && r.IsUnnecessary).Sum(r => r.Amount);
        if (total <= UnnecessaryShareLimit * income)
            return null;

        return new Warning(WarningRule.UnnecessaryShare,
            $"Gereksiz sayılan harcamalara bu ay {Tr.Money(total)} gitti; bu, gelirinin %{Tr.Percent(total / income)}'i " +
            "(sınır %20). Kemerleri biraz sıkma zamanı.");
    }

    // Rule 3: the saved expense's category is more than 130% of last month's total up to the same day.
    private async Task<Warning?> CategoryIncrease(string userId, List<MonthRow> rows, MonthRow saved, DateTime today)
    {
        var thisMonth = rows
            .Where(r => r.IsExpense && r.CategoryId == saved.CategoryId && r.Date.Date <= today.Date)
            .Sum(r => r.Amount);

        var previous = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        var cutoffDay = Math.Min(today.Day, DateTime.DaysInMonth(previous.Year, previous.Month));
        var lastMonthRows = await MonthData.GetMonthAsync(db, userId, previous.Year, previous.Month);
        var lastMonth = lastMonthRows
            .Where(r => r.IsExpense && r.CategoryId == saved.CategoryId && r.Date.Day <= cutoffDay)
            .Sum(r => r.Amount);

        if (lastMonth <= 0 || thisMonth <= CategoryIncreaseLimit * lastMonth)
            return null;

        return new Warning(WarningRule.CategoryIncrease,
            $"Bu ay {saved.CategoryName} için {Tr.Money(thisMonth)} harcadın; geçen ay aynı güne kadar " +
            $"{Tr.Money(lastMonth)} idi (%{Tr.Percent(thisMonth / lastMonth - 1m)} fazla).");
    }

    // Rule 4: the month-end projection is below zero. Without any known income this month (recorded, or still
    // expected from a recurring definition) the projection would just be "all spending is a deficit", so the
    // rule stays quiet until there is one.
    private async Task<Warning?> NegativeForecast(string userId, DateTime today)
    {
        var projection = await forecast.GetProjectedBalanceAsync(userId, today);
        if (projection is null || projection.KnownIncome <= 0 || projection.Projected >= 0)
            return null;

        return new Warning(WarningRule.NegativeForecast,
            $"Bu hızla ay sonunda {Tr.Money(-projection.Projected)} açık verirsin. Menüde makarna var.");
    }
}
