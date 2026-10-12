using BudgetApp.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>The university letter scale (AA … FF) for a 0–100 score.</summary>
public static class GradeScale
{
    private static readonly (int Min, string Letter)[] Steps =
    [
        (90, "AA"), (85, "BA"), (80, "BB"), (75, "CB"), (70, "CC"), (65, "DC"), (60, "DD"), (50, "FD"), (0, "FF")
    ];

    /// <summary>The score as shown: clamped to 0–100 and rounded to a whole number (halves go up).</summary>
    public static int Points(decimal score) =>
        (int)Math.Round(Math.Clamp(score, 0m, 100m), MidpointRounding.AwayFromZero);

    /// <summary>The letter of the rounded score, so the number and the letter on screen always agree.</summary>
    public static string LetterFor(decimal score)
    {
        var points = Points(score);
        return Steps.First(s => points >= s.Min).Letter;
    }

    /// <summary>0 for FF up to 8 for AA; the difference of two ranks is the number of steps between two letters.</summary>
    public static int Rank(string letter)
    {
        var index = Array.FindIndex(Steps, s => s.Letter == letter);
        if (index < 0)
            throw new ArgumentException($"'{letter}' is not a letter grade.", nameof(letter));
        return Steps.Length - 1 - index;
    }

    /// <summary>The lowest score of the next better letter; null when the score is already an AA.</summary>
    public static int? NextStepPoints(decimal score)
    {
        var points = Points(score);
        var better = Steps.Where(s => s.Min > points).ToList();
        return better.Count == 0 ? null : better[^1].Min;
    }
}

/// <summary>One category limit and what was spent in the category in the graded month.</summary>
public record BudgetFact(string Category, decimal Limit, decimal Spent, bool IsUnnecessary);

/// <summary>Everything the grade of a month is computed from.</summary>
/// <param name="AllowanceDays">Days of the month the daily allowance could be worked out for.</param>
/// <param name="AllowanceDaysKept">How many of those days the variable spending stayed within the allowance.</param>
public record MonthFacts(
    decimal Income, decimal Expense, decimal Unnecessary,
    IReadOnlyList<BudgetFact> Budgets, int AllowanceDays, int AllowanceDaysKept);

public enum CourseKind { Savings, Budgets, Unnecessary, Allowance }

/// <summary>One "course" of the report card.</summary>
/// <param name="Score">0–100; null when the course could not be graded (see <paramref name="NotGradedReason"/>).</param>
/// <param name="Value">What was measured, without any amount of money (e.g. "%12", "3/4 limit içinde").</param>
/// <param name="Trend">Against the compared month: 1 better letter, -1 worse, 0 same; null when there is nothing to compare.</param>
public record ReportCourse(
    CourseKind Kind, string Name, string Description, decimal Weight,
    decimal? Score, string Value, string? NotGradedReason = null, int? Trend = null)
{
    public bool IsGraded => Score is not null;
    public int? Points => Score is { } s ? GradeScale.Points(s) : null;
    public string? Letter => Score is { } s ? GradeScale.LetterFor(s) : null;
}

/// <summary>A category with a limit, graded on its own.</summary>
/// <param name="UsagePercent">Spent ÷ limit in percent; can be above 100.</param>
public record CategoryCourse(string Category, decimal UsagePercent, decimal Score)
{
    public int Points => GradeScale.Points(Score);
    public string Letter => GradeScale.LetterFor(Score);
}

public record Grading(decimal Score, IReadOnlyList<ReportCourse> Courses, IReadOnlyList<CategoryCourse> Categories)
{
    public int Points => GradeScale.Points(Score);
    public string Letter => GradeScale.LetterFor(Score);
}

/// <summary>The pure scoring rules of the report card (see docs/rules.md, section 9).</summary>
public static class ReportCardMath
{
    public const decimal SavingsWeight = 35m;
    public const decimal BudgetsWeight = 25m;
    public const decimal UnnecessaryWeight = 20m;
    public const decimal AllowanceWeight = 20m;

    /// <summary>The allowance course needs at least this many days to say anything.</summary>
    public const int MinAllowanceDays = 10;

    /// <summary>Unnecessary spending at this share of the income scores exactly a CC (the threshold of warning rule 2).</summary>
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

    /// <summary>The share of days the allowance was kept, as a score; null with fewer than <see cref="MinAllowanceDays"/> days.</summary>
    public static decimal? AllowanceScore(int daysKept, int days) =>
        days < MinAllowanceDays ? null : Clamp(daysKept * 100m / days);

    /// <summary>
    /// Grades a month with income. Courses that cannot be graded (no limits, too few allowance days) are left out of
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
        var allowance = AllowanceScore(facts.AllowanceDaysKept, facts.AllowanceDays);

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
                ? new(CourseKind.Allowance, "Günlük hak", "Günlük harcama hakkına uyulan günler", AllowanceWeight, null, "—",
                    $"Yalnızca {facts.AllowanceDays} gün hesaplanabildi; en az {MinAllowanceDays} gün gerekir.")
                : new(CourseKind.Allowance, "Günlük hak", "Günlük harcama hakkına uyulan günler", AllowanceWeight,
                    allowance, $"{facts.AllowanceDaysKept}/{facts.AllowanceDays} gün")
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
/// <param name="Steps">Letter steps between the two; positive when the grade went up.</param>
public record ReportComparison(int Year, int Month, string Letter, int Points, int Steps, string Text);

/// <summary>A concrete "what if" with the overall grade it would have given.</summary>
public record ReportSuggestion(string Text, string Letter, int Points);

/// <summary>The report card of one month. Without a grade, <see cref="Reason"/> says why.</summary>
public record ReportCard(
    int Year, int Month, ReportCardStatus Status, string? Reason, decimal? Score,
    IReadOnlyList<ReportCourse> Courses, IReadOnlyList<CategoryCourse> Categories,
    ReportComparison? Comparison, IReadOnlyList<ReportSuggestion> Suggestions)
{
    public bool IsGraded => Status == ReportCardStatus.Graded;
    public int? Points => Score is { } s ? GradeScale.Points(s) : null;
    public string? Letter => Score is { } s ? GradeScale.LetterFor(s) : null;
}

/// <summary>One line of the month list.</summary>
public record ReportCardSummary(int Year, int Month, ReportCardStatus Status, string? Letter, int? Points, string? Reason);

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
                result.Grading?.Letter, result.Grading?.Points, result.Reason));
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
        var (days, kept) = AllowanceDays(data, period);

        var facts = new MonthFacts(income, expenses.Sum(r => r.Amount),
            expenses.Where(r => r.IsUnnecessary).Sum(r => r.Amount), budgets, days, kept);
        return new(ReportCardStatus.Graded, null, facts, ReportCardMath.Grade(facts));
    }

    /// <summary>
    /// The daily allowance of every day of a past month, rebuilt from the transactions that exist now (the live card
    /// looks at the recurring definitions, which only know the present). A day is "kept" when its variable spending
    /// stayed within that day's allowance. The next income is the next recurring income of the same month, otherwise
    /// the month end, so the result of a finished month does not depend on what happens later.
    /// </summary>
    private static (int Days, int Kept) AllowanceDays(UserData data, DateTime period)
    {
        var end = period.AddMonths(1);
        var from = data.FirstDay is { } first && first > period ? first : period;

        var income = data.Rows.Where(r => !r.IsExpense && r.Date < from).Sum(r => r.Amount);
        var spent = data.Rows.Where(r => r.IsExpense && r.Date < from).Sum(r => r.Amount);
        var setAside = data.Contributions.Where(c => c.Date < from).Sum(c => c.Amount);
        var monthRows = data.Rows.Where(r => r.Date >= from && r.Date < end).ToList();
        var monthContributions = data.Contributions.Where(c => c.Date >= from && c.Date < end).ToList();

        var days = 0;
        var kept = 0;
        for (var day = from; day < end; day = day.AddDays(1))
        {
            var todays = monthRows.Where(r => r.Date.Date == day).ToList();
            income += todays.Where(r => !r.IsExpense).Sum(r => r.Amount);
            setAside += monthContributions.Where(c => c.Date.Date == day).Sum(c => c.Amount);
            var fixedToday = todays.Where(r => r.IsExpense && r.IsFixedLike).Sum(r => r.Amount);
            var variableToday = todays.Where(r => r.IsExpense && !r.IsFixedLike).Sum(r => r.Amount);

            var later = monthRows.Where(r => r.IsRecurring && r.Date.Date > day).ToList();
            var periodEnd = later.Where(r => !r.IsExpense).Select(r => (DateTime?)r.Date.Date).Min() ?? end;
            var upcoming = later.Where(r => r.IsExpense && r.Date.Date < periodEnd).Sum(r => r.Amount);

            var available = income - spent - fixedToday - setAside - upcoming;
            var allowance = available > 0 ? Math.Floor(available / (periodEnd - day).Days * 100m) / 100m : 0m;

            days++;
            if (variableToday <= allowance)
                kept++;
            spent += fixedToday + variableToday;
        }
        return (days, kept);
    }

    // ---------- comparison ----------

    private static int? Trend(ReportCourse current, ReportCourse previous) =>
        current.Letter is null || previous.Letter is null
            ? null
            : Math.Sign(GradeScale.Rank(current.Letter) - GradeScale.Rank(previous.Letter));

    private static ReportComparison Compare(DateTime period, Grading grading, DateTime earlier, Grading previous)
    {
        var steps = GradeScale.Rank(grading.Letter) - GradeScale.Rank(previous.Letter);
        var then = MonthLocatives[earlier.Month - 1] + (earlier.Year == period.Year ? "" : $" ({earlier.Year})");
        var now = MonthLocatives[period.Month - 1];
        var text = steps == 0
            ? $"{then} da {previous.Letter} almıştın; {now} notun aynı kaldı."
            : $"{then} {previous.Letter} almıştın, {now} {grading.Letter} aldın: {Math.Abs(steps)} basamak {(steps > 0 ? "yükseldin" : "düştün")}.";
        return new ReportComparison(earlier.Year, earlier.Month, previous.Letter, previous.Points, steps, text);
    }

    // ---------- suggestions ----------

    /// <summary>
    /// "What if" variants of the month, graded with the same rules; the one or two that would have raised the
    /// overall score the most are kept. The allowance course is left as it was.
    /// </summary>
    private static List<ReportSuggestion> Suggest(MonthFacts facts, Grading grading)
    {
        if (grading.Letter == "AA")
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

        // Unnecessary spending cut down to the share that scores a CC.
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

        // Enough saved for a better letter of the savings course: the nearest one that also moves the overall score.
        var savings = ReportCardMath.SavingsScore(facts.Income, facts.Expense);
        for (var next = GradeScale.NextStepPoints(savings); next is { } target; next = GradeScale.NextStepPoints(target))
        {
            var rate = target >= 60 ? (target - 60m) / 2m : (target - 60m) / 3m;
            var cut = RoundUp(rate / 100m * facts.Income - (facts.Income - facts.Expense));
            if (cut <= 0 || cut > facts.Expense)
                break;

            var result = ReportCardMath.Grade(facts with { Expense = facts.Expense - cut });
            if (result.Points > grading.Points)
            {
                candidates.Add((
                    $"Tasarruf notun {GradeScale.LetterFor(savings)}. {Tr.Money(cut)} daha az harcasaydın {GradeScale.LetterFor(target)} olurdu;",
                    result));
                break;
            }
        }

        return candidates
            .Where(c => c.Result.Points > grading.Points)
            .OrderByDescending(c => c.Result.Score)
            .Take(2)
            .Select(c => new ReportSuggestion(
                c.Result.Letter != grading.Letter
                    ? $"{c.Text} genel notun {grading.Letter} yerine {c.Result.Letter} olurdu."
                    : $"{c.Text} genel puanın {grading.Points} yerine {c.Result.Points} olurdu.",
                c.Result.Letter, c.Result.Points))
            .ToList();
    }

    private static decimal RoundUp(decimal value) => Math.Ceiling(value * 100m) / 100m;
}
