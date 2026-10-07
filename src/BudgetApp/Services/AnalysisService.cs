using BudgetApp.Data;

namespace BudgetApp.Services;

public record CategoryTotal(string Name, decimal Amount);

public record ItemTotal(string ItemName, decimal Amount, int Count);

public record CategoryChange(string Name, decimal Current, decimal Previous, bool IsUnnecessary)
{
    public decimal Delta => Current - Previous;

    /// <summary>Change relative to the previous month; null when there was no spending then.</summary>
    public decimal? Percent => Previous == 0m ? null : Delta / Previous * 100m;
}

public record MonthlyAnalysis(
    int Year,
    int Month,
    decimal TotalExpense,
    decimal PreviousTotalExpense,
    List<CategoryTotal> Categories,
    List<ItemTotal> TopItems,
    List<CategoryChange> Changes,
    List<string> Advice);

public interface IAnalysisService
{
    Task<MonthlyAnalysis> GetMonthlyAnalysisAsync(string userId, int year, int month, DateTime today);
}

public class AnalysisService(ApplicationDbContext db, IForecastService forecast) : IAnalysisService
{
    private const int TopItemCount = 10;
    private const int FrequentItemMinCount = 3;
    private const int MaxAdvice = 3;

    public async Task<MonthlyAnalysis> GetMonthlyAnalysisAsync(string userId, int year, int month, DateTime today)
    {
        var previousMonth = new DateTime(year, month, 1).AddMonths(-1);
        var current = (await MonthData.GetMonthAsync(db, userId, year, month)).Where(r => r.IsExpense).ToList();
        var previous = (await MonthData.GetMonthAsync(db, userId, previousMonth.Year, previousMonth.Month))
            .Where(r => r.IsExpense).ToList();

        var categories = current
            .GroupBy(r => r.CategoryName)
            .Select(g => new CategoryTotal(g.Key, g.Sum(r => r.Amount)))
            .OrderByDescending(c => c.Amount)
            .ThenBy(c => c.Name)
            .ToList();

        // Fixed expenses (rent, bills, subscriptions) are not "products" and would crowd out the real top items.
        var topItems = current
            .Where(r => !r.IsFixed && !string.IsNullOrWhiteSpace(r.ItemName))
            .GroupBy(r => r.ItemName!.Trim())
            .Select(g => new ItemTotal(g.Key, g.Sum(r => r.Amount), g.Count()))
            .OrderByDescending(i => i.Amount)
            .ThenBy(i => i.ItemName)
            .Take(TopItemCount)
            .ToList();

        var changes = BuildChanges(current, previous);
        var total = current.Sum(r => r.Amount);
        var previousTotal = previous.Sum(r => r.Amount);

        var advice = total > 0
            ? await BuildAdviceAsync(userId, today, total, previousTotal, categories, topItems, changes)
            : [];

        return new MonthlyAnalysis(year, month, total, previousTotal, categories, topItems, changes, advice);
    }

    private static List<CategoryChange> BuildChanges(List<MonthRow> current, List<MonthRow> previous)
    {
        var currentByCategory = current.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key);
        var previousByCategory = previous.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key);

        return currentByCategory.Keys.Union(previousByCategory.Keys)
            .Select(id =>
            {
                var cur = currentByCategory.GetValueOrDefault(id);
                var prev = previousByCategory.GetValueOrDefault(id);
                var sample = (cur ?? prev)!.First();
                return new CategoryChange(
                    sample.CategoryName,
                    cur?.Sum(r => r.Amount) ?? 0m,
                    prev?.Sum(r => r.Amount) ?? 0m,
                    sample.IsUnnecessary);
            })
            .OrderByDescending(c => c.Delta)
            .ThenBy(c => c.Name)
            .ToList();
    }

    private async Task<List<string>> BuildAdviceAsync(
        string userId, DateTime today, decimal total, decimal previousTotal,
        List<CategoryTotal> categories, List<ItemTotal> topItems, List<CategoryChange> changes)
    {
        var advice = new List<string>();

        // 1. Halve the unnecessary category that grew the most (or, if none grew, the largest one).
        var target = changes
            .Where(c => c.IsUnnecessary && c.Current > 0)
            .OrderByDescending(c => c.Delta)
            .ThenByDescending(c => c.Current)
            .FirstOrDefault();
        if (target is not null)
            advice.Add(await HalvingAdviceAsync(userId, today, target));

        // 2. An item bought again and again.
        var frequent = topItems.Where(i => i.Count >= FrequentItemMinCount).OrderByDescending(i => i.Amount).FirstOrDefault();
        if (frequent is not null)
            advice.Add($"{frequent.ItemName} için bu ay {frequent.Count} kez toplam {Tr.Money(frequent.Amount)} ödedin.");

        // 3. General observations fill the list up to the maximum so there are always at least two tips.
        if (advice.Count < MaxAdvice)
        {
            var top = categories[0];
            advice.Add($"Giderlerinin %{Tr.Percent(top.Amount / total)} kadarı {top.Name} kategorisine gitti.");
        }

        if (advice.Count < MaxAdvice)
        {
            if (previousTotal <= 0)
                advice.Add($"Bu ay toplam {Tr.Money(total)} harcadın.");
            else if (total == previousTotal)
                advice.Add($"Bu ay toplam {Tr.Money(total)} harcadın; geçen ayla aynı.");
            else
            {
                var percent = Tr.Percent(Math.Abs(total - previousTotal) / previousTotal);
                var direction = total > previousTotal ? "fazla" : "az";
                advice.Add($"Bu ay toplam {Tr.Money(total)} harcadın; geçen aya göre %{percent} {direction}.");
            }
        }

        return advice;
    }

    private async Task<string> HalvingAdviceAsync(string userId, DateTime today, CategoryChange category)
    {
        var saving = category.Current / 2m;

        var baseline = await forecast.GetGoalForecastAsync(userId, today);
        var withSaving = await forecast.GetGoalForecastAsync(userId, today, saving);
        if (baseline?.Status == GoalForecastStatus.Reachable && withSaving?.Status == GoalForecastStatus.Reachable)
        {
            var monthsEarlier = baseline.Months - withSaving.Months;
            if (monthsEarlier >= 1)
                return $"{category.Name} harcamanı yarıya indirsen hedefine {monthsEarlier} ay erken ulaşırsın.";
        }

        return $"{category.Name} harcamanı yarıya indirsen ayda {Tr.Money(saving)} cebinde kalır.";
    }
}
