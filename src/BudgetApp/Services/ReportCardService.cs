using BudgetApp.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>
/// The five levels of a 0–100 score: Pekiyi (85–100), İyi (70–84), Orta (55–69), Geçer (45–54), Zayıf (0–44).
/// The familiar school-report words, so the card reads the same for a student and for someone with a salary.
/// </summary>
public static class GradeScale
{
    public const string Top = "Pekiyi";

    private static readonly (int Min, string Level)[] Steps =
    [
        (85, Top), (70, "İyi"), (55, "Orta"), (45, "Geçer"), (0, "Zayıf")
    ];

    /// <summary>The score as shown: clamped to 0–100 and rounded to a whole number (halves go up).</summary>
    public static int Points(decimal score) =>
        (int)Math.Round(Math.Clamp(score, 0m, 100m), MidpointRounding.AwayFromZero);

    /// <summary>The level of the rounded score, so the number and the level on screen always agree.</summary>
    public static string LevelFor(decimal score)
    {
        var points = Points(score);
        return Steps.First(s => points >= s.Min).Level;
    }

    /// <summary>0 for Zayıf up to 4 for Pekiyi.</summary>
    public static int Rank(string level)
    {
        var index = Array.FindIndex(Steps, s => s.Level == level);
        if (index < 0)
            throw new ArgumentException($"'{level}' is not a level.", nameof(level));
        return Steps.Length - 1 - index;
    }
}

/// <summary>One category limit and what was spent in the category in the graded month.</summary>
public record BudgetFact(string Category, decimal Limit, decimal Spent, bool IsUnnecessary);

/// <summary>Everything the grade of a month is computed from.</summary>
/// <param name="Weeks">The weeks of the month, for the allowance course.</param>
public record MonthFacts(
    decimal Income, decimal Expense, decimal Unnecessary,
    IReadOnlyList<BudgetFact> Budgets, IReadOnlyList<WeekFact> Weeks);

/// <summary>One week of the graded month.</summary>
/// <param name="Budget">The daily allowances of the week's days added up.</param>
/// <param name="Spent">The week's variable spending.</param>
public record WeekFact(decimal Budget, decimal Spent)
{
    /// <summary>The week stayed within its daily allowances.</summary>
    public bool IsKept => Spent <= Budget;
}

public enum CourseKind { Savings, Budgets, Unnecessary, Allowance }

/// <summary>One "course" of the report card.</summary>
/// <param name="Score">0–100; null when the course could not be graded (see <paramref name="NotGradedReason"/>).</param>
/// <param name="Value">What was measured, without any amount of money (e.g. "%12", "3/4 limit içinde").</param>
/// <param name="Trend">Against the compared month: 1 better level, -1 worse, 0 same; null when there is nothing to compare.</param>
public record ReportCourse(
    CourseKind Kind, string Name, string Description, decimal Weight,
    decimal? Score, string Value, string? NotGradedReason = null, int? Trend = null)
{
    public bool IsGraded => Score is not null;
    public int? Points => Score is { } s ? GradeScale.Points(s) : null;
    public string? Level => Score is { } s ? GradeScale.LevelFor(s) : null;
}

/// <summary>A category with a limit, graded on its own.</summary>
/// <param name="UsagePercent">Spent ÷ limit in percent; can be above 100.</param>
public record CategoryCourse(string Category, decimal UsagePercent, decimal Score)
{
    public int Points => GradeScale.Points(Score);
    public string Level => GradeScale.LevelFor(Score);
}

public record Grading(decimal Score, IReadOnlyList<ReportCourse> Courses, IReadOnlyList<CategoryCourse> Categories)
{
    public int Points => GradeScale.Points(Score);
    public string Level => GradeScale.LevelFor(Score);
}

/// <summary>The pure scoring rules of the report card (see docs/rules.md, section 9).</summary>
public static class ReportCardMath
{
    public const decimal SavingsWeight = 35m;
    public const decimal BudgetsWeight = 25m;
    public const decimal UnnecessaryWeight = 20m;
    public const decimal AllowanceWeight = 20m;

    /// <summary>The allowance course needs at least this many weeks to say anything.</summary>
    public const int MinAllowanceWeeks = 2;

    /// <summary>Unnecessary spending at this share of the income scores exactly 70 (the threshold of warning rule 2).</summary>
    public const decimal UnnecessaryTargetShare = 0.20m;

    /// <summary>Breaking even is 60; saving 20% of the income is 100; a deficit loses 3 points per percent.</summary>
    public static decimal SavingsScore(decimal income, decimal expense)
    {
        var rate = (income - expense) / income * 100m;
        return Clamp(rate >= 0 ? 60m + 2m * rate : 60m + 3m * rate);
    }

    /// <summary>Up to 80% of the limit is 100; every percent above costs 1.5 points (exactly the limit is 70).</summary>
    public static decimal BudgetScore(decimal spent, decimal limit) =>
        Clamp(100m - 1.5m * (spent / limit * 100m - 80m));

    /// <summary>Up to 10% of the income is 100; every percent above costs 3 points (20% is 70).</summary>
    public static decimal UnnecessaryScore(decimal unnecessary, decimal income) =>
        Clamp(100m - 3m * (unnecessary / income * 100m - 10m));

    /// <summary>
    /// A week within its daily allowances is 100. A week above them loses 2 points for every percent it is over
    /// (10% over is 80, 50% over is 0), so a near miss is not counted like a week that got out of hand.
    /// </summary>
    public static decimal WeekScore(decimal spent, decimal budget)
    {
        if (spent <= budget)
            return 100m;
        return budget <= 0 ? 0m : Clamp(100m - 2m * (spent / budget * 100m - 100m));
    }

    /// <summary>The average of the weeks' scores; null with fewer than <see cref="MinAllowanceWeeks"/> weeks.</summary>
    public static decimal? AllowanceScore(IReadOnlyList<WeekFact> weeks) =>
        weeks.Count < MinAllowanceWeeks ? null : weeks.Average(w => WeekScore(w.Spent, w.Budget));

    /// <summary>
    /// Grades a month with income. Courses that cannot be graded (no limits, too few allowance weeks) are left out of
    /// the overall score and their weight is shared among the others in proportion.
    /// </summary>
    public static Grading Grade(MonthFacts facts)
    {
        if (facts.Income <= 0)
            throw new ArgumentException("A month without income cannot be graded.", nameof(facts));

        var categories = facts.Budgets
            .Where(b => b.Limit > 0)
            .Select(b => new CategoryCourse(b.Category, b.Spent / b.Limit * 100m, BudgetScore(b.Spent, b.Limit)))
            .OrderBy(c => c.Score).ThenBy(c => c.Category, StringComparer.Create(Tr.Culture, false))
            .ToList();

        var rate = (facts.Income - facts.Expense) / facts.Income;
        var share = facts.Unnecessary / facts.Income;
        var allowance = AllowanceScore(facts.Weeks);

        var courses = new List<ReportCourse>
        {
            new(CourseKind.Savings, "Tasarruf", "Gelirden artan pay", SavingsWeight,
                SavingsScore(facts.Income, facts.Expense), PercentText(rate)),
            categories.Count == 0
                ? new(CourseKind.Budgets, "Bütçe uyumu", "Limit içinde kalan kategoriler", BudgetsWeight, null, "—",
                    "Kategori limitin yok; limit koyarsan bu ders de notlanır.")
                : new(CourseKind.Budgets, "Bütçe uyumu", "Limit içinde kalan kategoriler", BudgetsWeight,
                    categories.Average(c => c.Score),
                    $"{categories.Count(c => c.UsagePercent <= 100m)}/{categories.Count} limit içinde"),
            new(CourseKind.Unnecessary, "Gereksiz harcama", "Gereksiz kategorilerin gelire oranı", UnnecessaryWeight,
                UnnecessaryScore(facts.Unnecessary, facts.Income), PercentText(share)),
            allowance is null
                ? new(CourseKind.Allowance, "Günlük hak", "Harcaması günlük hakların toplamını aşmayan haftalar", AllowanceWeight, null, "—",
                    $"Yalnızca {facts.Weeks.Count} hafta hesaplanabildi; en az {MinAllowanceWeeks} hafta gerekir.")
                : new(CourseKind.Allowance, "Günlük hak", "Harcaması günlük hakların toplamını aşmayan haftalar", AllowanceWeight,
                    allowance, $"{facts.Weeks.Count(w => w.IsKept)}/{facts.Weeks.Count} hafta")
        };

        var graded = courses.Where(c => c.IsGraded).ToList();
        var overall = graded.Sum(c => c.Weight * c.Score!.Value) / graded.Sum(c => c.Weight);
        return new Grading(overall, courses, categories);
    }

    private static decimal Clamp(decimal score) => Math.Clamp(score, 0m, 100m);

    private static string PercentText(decimal ratio)
    {
        var percent = Tr.Percent(ratio);
        return percent < 0 ? $"-%{-percent}" : $"%{percent}";
    }
}

public enum ReportCardStatus
{
    Graded,
    /// <summary>The month is the current one or in the future.</summary>
    NotFinished,
    /// <summary>No transactions in the month.</summary>
    NoData,
    NoIncome,
    /// <summary>Expenses on too few days to be fair.</summary>
    TooLittleData
}

/// <summary>The month the card is compared with: the nearest earlier month that got a grade.</summary>
/// <param name="Change">Points between the two; positive when the score went up.</param>
public record ReportComparison(int Year, int Month, string Level, int Points, int Change, string Text);

/// <summary>A concrete "what if" with the overall grade it would have given.</summary>
public record ReportSuggestion(string Text, string Level, int Points);

/// <summary>The report card of one month. Without a grade, <see cref="Reason"/> says why.</summary>
public record ReportCard(
    int Year, int Month, ReportCardStatus Status, string? Reason, decimal? Score,
    IReadOnlyList<ReportCourse> Courses, IReadOnlyList<CategoryCourse> Categories,
    ReportComparison? Comparison, IReadOnlyList<ReportSuggestion> Suggestions)
{
    public bool IsGraded => Status == ReportCardStatus.Graded;
    public int? Points => Score is { } s ? GradeScale.Points(s) : null;
    public string? Level => Score is { } s ? GradeScale.LevelFor(s) : null;
}

/// <summary>One line of the month list.</summary>
public record ReportCardSummary(int Year, int Month, ReportCardStatus Status, string? Level, int? Points, string? Reason);

public interface IReportCardService
{
    /// <summary>The report card of a month. Only finished months are graded; nothing is stored, it is computed from the transactions.</summary>
    Task<ReportCard> GetAsync(string userId, int year, int month, DateTime today);

    /// <summary>The finished months since the user's first transaction, newest first, at most <paramref name="months"/>.</summary>
    Task<List<ReportCardSummary>> GetHistoryAsync(string userId, DateTime today, int months = 12);
}

public class ReportCardService(ApplicationDbContext db) : IReportCardService
{
    /// <summary>A month needs expenses on at least this many different days.</summary>
    public const int MinExpenseDays = 5;

    /// <summary>A piece of a week at the start or the end of the month counts as a week of its own from this many days.</summary>
    public const int MinWeekDays = 4;

    /// <summary>The savings suggestion aims at the next multiple of this many points.</summary>
    private const int SavingsStep = 5;

    /// <summary>How far back the month to compare with is looked for.</summary>
    private const int ComparisonMonths = 12;

    private static readonly string[] MonthLocatives =
    [
        "Ocak'ta", "Şubat'ta", "Mart'ta", "Nisan'da", "Mayıs'ta", "Haziran'da",
        "Temmuz'da", "Ağustos'ta", "Eylül'de", "Ekim'de", "Kasım'da", "Aralık'ta"
    ];

    public async Task<ReportCard> GetAsync(string userId, int year, int month, DateTime today)
    {
        var data = await LoadAsync(userId, today);
        var period = new DateTime(year, month, 1);
        var result = Evaluate(data, period);
        if (result.Grading is not { } grading)
            return new ReportCard(year, month, result.Status, result.Reason, null, [], [], null, []);

        // The nearest earlier month with a grade; months without one (no income, summer break) are skipped.
        (DateTime Period, Grading Grading)? previous = null;
        for (var i = 1; i <= ComparisonMonths && previous is null; i++)
        {
            var earlier = period.AddMonths(-i);
            if (data.FirstDay is null || earlier < RecurringQueries.Period(data.FirstDay.Value))
                break;
            if (Evaluate(data, earlier).Grading is { } g)
                previous = (earlier, g);
        }

        var courses = grading.Courses;
        ReportComparison? comparison = null;
        if (previous is { } p)
        {
            courses = courses.Select(c => c with { Trend = Trend(c, p.Grading.Courses.First(x => x.Kind == c.Kind)) }).ToList();
            comparison = Compare(period, grading, p.Period, p.Grading);
        }

        return new ReportCard(year, month, ReportCardStatus.Graded, null, grading.Score, courses, grading.Categories,
            comparison, Suggest(result.Facts!, grading));
    }

    public async Task<List<ReportCardSummary>> GetHistoryAsync(string userId, DateTime today, int months = 12)
    {
        var data = await LoadAsync(userId, today);
        var summaries = new List<ReportCardSummary>();
        if (data.FirstDay is null)
            return summaries;

        var first = RecurringQueries.Period(data.FirstDay.Value);
        for (var period = data.CurrentPeriod.AddMonths(-1); period >= first && summaries.Count < months; period = period.AddMonths(-1))
        {
            var result = Evaluate(data, period);
            summaries.Add(new ReportCardSummary(period.Year, period.Month, result.Status,
                result.Grading?.Level, result.Grading?.Points, result.Reason));
        }
        return summaries;
    }

    // ---------- data ----------

    private record UserData(
        DateTime CurrentPeriod, DateTime? FirstDay, List<MonthRow> Rows,
        List<(DateTime Date, decimal Amount)> Contributions, List<BudgetLimit> Budgets);

    private record BudgetLimit(int CategoryId, string Category, bool IsUnnecessary, decimal Limit);

    private record Evaluation(ReportCardStatus Status, string? Reason, MonthFacts? Facts, Grading? Grading);

    private async Task<UserData> LoadAsync(string userId, DateTime today)
    {
        // One query for the whole history: the allowance course needs the all-time balance of every graded day.
        var rows = await MonthData.GetRowsAsync(db, userId, DateTime.MinValue, today.Date.AddDays(1));
        var contributions = (await db.GoalContributions
                .AsNoTracking()
                .Where(c => c.Goal!.UserId == userId)
                .Select(c => new { c.Date, c.Amount })
                .ToListAsync())
            .Select(c => (c.Date, c.Amount))
            .ToList();
        var budgets = await db.CategoryBudgets
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => new BudgetLimit(b.CategoryId, b.Category!.Name, b.Category.IsUnnecessary, b.MonthlyLimit))
            .ToListAsync();

        DateTime? firstDay = rows.Count == 0 ? null : rows.Min(r => r.Date).Date;
        return new UserData(RecurringQueries.Period(today), firstDay, rows, contributions, budgets);
    }

    private static Evaluation Evaluate(UserData data, DateTime period)
    {
        if (period >= data.CurrentPeriod)
            return new(ReportCardStatus.NotFinished, $"{Tr.MonthYear(period)} henüz bitmedi; karne ay bitince hazırlanır.", null, null);

        var end = period.AddMonths(1);
        var rows = data.Rows.Where(r => r.Date >= period && r.Date < end).ToList();
        if (rows.Count == 0)
            return new(ReportCardStatus.NoData, "Bu ay hiç işlem kaydı yok.", null, null);

        var income = MonthData.Income(rows);
        if (income <= 0)
            return new(ReportCardStatus.NoIncome, "Bu ay gelir kaydı olmadığı için tasarruf oranı hesaplanamadı; not verilmedi.", null, null);

        var expenses = rows.Where(r => r.IsExpense).ToList();
        var expenseDays = expenses.Select(r => r.Date.Date).Distinct().Count();
        if (expenseDays < MinExpenseDays)
            return new(ReportCardStatus.TooLittleData,
                $"Yalnızca {expenseDays} günde gider kaydı var; adil bir not için en az {MinExpenseDays} gün gerekir.", null, null);

        var spentByCategory = expenses.GroupBy(r => r.CategoryId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var budgets = data.Budgets
            .Select(b => new BudgetFact(b.Category, b.Limit, spentByCategory.GetValueOrDefault(b.CategoryId), b.IsUnnecessary))
            .ToList();
        var facts = new MonthFacts(income, expenses.Sum(r => r.Amount),
            expenses.Where(r => r.IsUnnecessary).Sum(r => r.Amount), budgets, AllowanceWeeks(data, period));
        return new(ReportCardStatus.Graded, null, facts, ReportCardMath.Grade(facts));
    }

    /// <summary>
    /// The allowance course, week by week: each week's variable spending next to the week's budget, which is the
    /// daily allowances of its days added up. A single expensive day is fine as long as the week evens it out.
    ///
    /// The allowances are rebuilt from the transactions that exist now (the live card looks at the recurring
    /// definitions, which only know the present). Each week starts from the money that was really there that
    /// morning; inside the week every day is assumed to spend exactly its allowance, so the budget is the plan of
    /// the week and does not grow or shrink with how the week was actually spent. The next income is the next
    /// recurring income of the same month, otherwise the month end, so a finished month does not depend on later ones.
    /// </summary>
    private static List<WeekFact> AllowanceWeeks(UserData data, DateTime period)
    {
        var end = period.AddMonths(1);
        var from = data.FirstDay is { } first && first > period ? first : period;
        var monthRows = data.Rows.Where(r => r.Date >= from && r.Date < end).ToList();
        var monthContributions = data.Contributions.Where(c => c.Date >= from && c.Date < end).ToList();

        var weeks = new List<WeekFact>();
        foreach (var (start, stop) in Weeks(from, end))
        {
            var income = data.Rows.Where(r => !r.IsExpense && r.Date < start).Sum(r => r.Amount);
            var spent = data.Rows.Where(r => r.IsExpense && r.Date < start).Sum(r => r.Amount);
            var setAside = data.Contributions.Where(c => c.Date < start).Sum(c => c.Amount);

            var budget = 0m;
            var actual = 0m;
            for (var day = start; day < stop; day = day.AddDays(1))
            {
                var todays = monthRows.Where(r => r.Date.Date == day).ToList();
                income += todays.Where(r => !r.IsExpense).Sum(r => r.Amount);
                setAside += monthContributions.Where(c => c.Date.Date == day).Sum(c => c.Amount);
                var fixedToday = todays.Where(r => r.IsExpense && r.IsFixedLike).Sum(r => r.Amount);
                actual += todays.Where(r => r.IsExpense && !r.IsFixedLike).Sum(r => r.Amount);

                var later = monthRows.Where(r => r.IsRecurring && r.Date.Date > day).ToList();
                var periodEnd = later.Where(r => !r.IsExpense).Select(r => (DateTime?)r.Date.Date).Min() ?? end;
                var upcoming = later.Where(r => r.IsExpense && r.Date.Date < periodEnd).Sum(r => r.Amount);

                var available = income - spent - fixedToday - setAside - upcoming;
                var allowance = available > 0 ? Math.Floor(available / (periodEnd - day).Days * 100m) / 100m : 0m;

                budget += allowance;
                spent += fixedToday + allowance;
            }

            weeks.Add(new WeekFact(budget, actual));
        }
        return weeks;
    }

    /// <summary>
    /// The calendar weeks (Monday to Sunday) of a range. The pieces at the two ends are rarely whole weeks: one of
    /// at least <see cref="MinWeekDays"/> days is judged on its own, a shorter one is joined to the week next to it.
    /// </summary>
    private static List<(DateTime Start, DateTime End)> Weeks(DateTime from, DateTime end)
    {
        var weeks = new List<(DateTime Start, DateTime End)>();
        for (var start = from; start < end;)
        {
            var toMonday = ((int)DayOfWeek.Monday - (int)start.DayOfWeek + 7) % 7;
            var stop = start.AddDays(toMonday == 0 ? 7 : toMonday);
            if (stop > end)
                stop = end;
            weeks.Add((start, stop));
            start = stop;
        }

        if (weeks.Count > 1 && (weeks[0].End - weeks[0].Start).Days < MinWeekDays)
        {
            weeks[1] = (weeks[0].Start, weeks[1].End);
            weeks.RemoveAt(0);
        }
        if (weeks.Count > 1 && (weeks[^1].End - weeks[^1].Start).Days < MinWeekDays)
        {
            weeks[^2] = (weeks[^2].Start, weeks[^1].End);
            weeks.RemoveAt(weeks.Count - 1);
        }
        return weeks;
    }

    // ---------- comparison ----------

    private static int? Trend(ReportCourse current, ReportCourse previous) =>
        current.Points is { } now && previous.Points is { } then ? Math.Sign(now - then) : null;

    private static ReportComparison Compare(DateTime period, Grading grading, DateTime earlier, Grading previous)
    {
        var change = grading.Points - previous.Points;
        var then = MonthLocatives[earlier.Month - 1] + (earlier.Year == period.Year ? "" : $" ({earlier.Year})");
        var now = MonthLocatives[period.Month - 1];
        var text = change == 0
            ? $"{then} da {previous.Points} puan almıştın ({previous.Level}); {now} puanın aynı kaldı."
            : $"{then} {previous.Points} puan almıştın ({previous.Level}), {now} {grading.Points} puan aldın ({grading.Level}): " +
              $"{Math.Abs(change)} puan {(change > 0 ? "yükseldin" : "düştün")}.";
        return new ReportComparison(earlier.Year, earlier.Month, previous.Level, previous.Points, change, text);
    }

    // ---------- suggestions ----------

    /// <summary>
    /// "What if" variants of the month, graded with the same rules; the one or two that would have raised the
    /// overall score the most are kept. The allowance course is left as it was.
    /// </summary>
    private static List<ReportSuggestion> Suggest(MonthFacts facts, Grading grading)
    {
        if (grading.Level == GradeScale.Top)
            return [];

        var candidates = new List<(string Text, Grading Result)>();

        // The limit that was exceeded with the most to gain, pulled back to the limit.
        var budget = facts.Budgets
            .Where(b => b.Limit > 0 && b.Spent > b.Limit)
            .Select(b =>
            {
                var excess = b.Spent - b.Limit;
                var changed = facts with
                {
                    Expense = facts.Expense - excess,
                    Unnecessary = b.IsUnnecessary ? facts.Unnecessary - excess : facts.Unnecessary,
                    Budgets = facts.Budgets.Select(x => x == b ? x with { Spent = x.Limit } : x).ToList()
                };
                return (Budget: b, Excess: excess, Result: ReportCardMath.Grade(changed));
            })
            .OrderByDescending(x => x.Result.Score)
            .FirstOrDefault();
        if (budget.Budget is not null)
            candidates.Add(($"{budget.Budget.Category} limitini {Tr.Money(budget.Excess)} aştın. Limitte kalsaydın", budget.Result));

        // Unnecessary spending cut down to the threshold of warning rule 2.
        var targetUnnecessary = ReportCardMath.UnnecessaryTargetShare * facts.Income;
        if (facts.Unnecessary > targetUnnecessary)
        {
            var cut = RoundUp(facts.Unnecessary - targetUnnecessary);
            var changed = facts with { Expense = facts.Expense - cut, Unnecessary = facts.Unnecessary - cut };
            candidates.Add((
                $"Gereksiz harcama payın %{Tr.Percent(facts.Unnecessary / facts.Income)}. {Tr.Money(cut)} daha az harcasaydın " +
                $"%{Tr.Percent(ReportCardMath.UnnecessaryTargetShare)}'ye inerdi;",
                ReportCardMath.Grade(changed)));
        }

        // Enough saved for a rounder savings score: the nearest multiple of five that also moves the overall points.
        var savings = GradeScale.Points(ReportCardMath.SavingsScore(facts.Income, facts.Expense));
        for (var target = savings / SavingsStep * SavingsStep + SavingsStep; target <= 100; target += SavingsStep)
        {
            var rate = target >= 60 ? (target - 60m) / 2m : (target - 60m) / 3m;
            var cut = RoundUp(rate / 100m * facts.Income - (facts.Income - facts.Expense));
            if (cut <= 0 || cut > facts.Expense)
                break;

            var result = ReportCardMath.Grade(facts with { Expense = facts.Expense - cut });
            if (result.Points > grading.Points)
            {
                candidates.Add(($"Tasarruf puanın {savings}. {Tr.Money(cut)} daha az harcasaydın {target} olurdu;", result));
                break;
            }
        }

        return candidates
            .Where(c => c.Result.Points > grading.Points)
            .OrderByDescending(c => c.Result.Score)
            .Take(2)
            .Select(c => new ReportSuggestion(
                c.Result.Level != grading.Level
                    ? $"{c.Text} genel puanın {grading.Points} yerine {c.Result.Points}, seviyen {grading.Level} yerine {c.Result.Level} olurdu."
                    : $"{c.Text} genel puanın {grading.Points} yerine {c.Result.Points} olurdu.",
                c.Result.Level, c.Result.Points))
            .ToList();
    }

    private static decimal RoundUp(decimal value) => Math.Ceiling(value * 100m) / 100m;
}
