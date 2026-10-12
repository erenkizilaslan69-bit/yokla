using BudgetApp.Models;
using BudgetApp.Services;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class ReportCardServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Today = new(2026, 10, 12);
    private static readonly DateTime July = new(2026, 7, 1);
    private static readonly DateTime August = new(2026, 8, 1);
    private static readonly DateTime September = new(2026, 9, 1);

    private static DateTime Sep(int day) => new(2026, 9, day);

    private static Task<ReportCard> Get(TestDb t, DateTime? month = null, DateTime? today = null, string user = Alice)
    {
        var m = month ?? September;
        return new ReportCardService(t.Context).GetAsync(user, m.Year, m.Month, today ?? Today);
    }

    private static ReportCourse Course(ReportCard card, CourseKind kind) => card.Courses.Single(c => c.Kind == kind);

    /// <summary>The same amount on each of the given days of a month.</summary>
    private static void Spend(TestDb t, DateTime month, decimal amount, int category, params int[] days)
    {
        foreach (var day in days)
            AddExpense(t.Context, Alice, amount, new DateTime(month.Year, month.Month, day), category);
    }

    /// <summary>
    /// 1.000 in on the 1st and the same grocery amount on five days. Without limits and unnecessary spending the
    /// grade follows from the amount: 20 a day is an AA, 190 a BB, 210 (a deficit) a CC.
    /// </summary>
    private static void SimpleMonth(TestDb t, DateTime month, decimal perDay)
    {
        AddIncome(t.Context, Alice, 1000m, month);
        Spend(t, month, perDay, Market, 3, 7, 11, 15, 19);
    }

    private static void AddGeneratedIncome(TestDb t, decimal amount, DateTime date)
    {
        t.Context.Transactions.Add(new Transaction
        {
            UserId = Alice, Type = TransactionType.Income, Amount = amount, Date = date, CategoryId = TestData.Income,
            RecurringPeriod = new DateTime(date.Year, date.Month, 1)
        });
        t.Context.SaveChanges();
    }

    // ---------- the level scale ----------

    [Theory]
    [InlineData(100, "Pekiyi")]
    [InlineData(85, "Pekiyi")]
    [InlineData(84, "İyi")]
    [InlineData(70, "İyi")]
    [InlineData(69, "Orta")]
    [InlineData(55, "Orta")]
    [InlineData(54, "Geçer")]
    [InlineData(45, "Geçer")]
    [InlineData(44, "Zayıf")]
    [InlineData(0, "Zayıf")]
    public void LevelFor_FollowsTheFiveLevels(int score, string level) =>
        Assert.Equal(level, GradeScale.LevelFor(score));

    [Theory]
    [InlineData("84.5", "Pekiyi")]   // the level comes from the rounded score, the one that is shown
    [InlineData("84.49", "İyi")]
    [InlineData("44.5", "Geçer")]
    [InlineData("44.4", "Zayıf")]
    [InlineData("-5", "Zayıf")]
    [InlineData("120", "Pekiyi")]
    public void LevelFor_RoundsBeforeChoosingTheLevel(string score, string level) =>
        Assert.Equal(level, GradeScale.LevelFor(decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("89.5", 90)]
    [InlineData("89.49", 89)]
    [InlineData("80.5", 81)]
    [InlineData("-3", 0)]
    [InlineData("140", 100)]
    public void Points_RoundsHalvesUpAndStaysBetweenZeroAndAHundred(string score, int points) =>
        Assert.Equal(points, GradeScale.Points(decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Rank_OrdersTheLevelsFromZayifToPekiyi()
    {
        Assert.Equal(0, GradeScale.Rank("Zayıf"));
        Assert.Equal(1, GradeScale.Rank("Geçer"));
        Assert.Equal(2, GradeScale.Rank("Orta"));
        Assert.Equal(3, GradeScale.Rank("İyi"));
        Assert.Equal(4, GradeScale.Rank("Pekiyi"));
        Assert.Equal(4, GradeScale.Rank(GradeScale.Top));
        Assert.Throws<ArgumentException>(() => GradeScale.Rank("AA"));
    }

    // ---------- the score of each course ----------

    [Theory]
    [InlineData(1000, 1000, 60)]   // breaking even just passes
    [InlineData(1000, 950, 70)]
    [InlineData(1000, 900, 80)]
    [InlineData(1000, 800, 100)]   // a fifth of the income saved
    [InlineData(1000, 500, 100)]   // never more than 100
    [InlineData(1000, 1100, 30)]   // a deficit costs 3 points per percent
    [InlineData(1000, 1200, 0)]
    [InlineData(1000, 3000, 0)]
    public void SavingsScore_RewardsTheShareOfIncomeThatIsLeft(int income, int expense, int score) =>
        Assert.Equal(score, ReportCardMath.SavingsScore(income, expense));

    [Theory]
    [InlineData(0, 500, 100)]
    [InlineData(400, 500, 100)]    // up to 80% of the limit
    [InlineData(450, 500, 85)]
    [InlineData(500, 500, 70)]     // exactly the limit
    [InlineData(550, 500, 55)]
    [InlineData(570, 500, 49)]
    [InlineData(1000, 500, 0)]
    public void BudgetScore_LosesPointsAboveEightyPercentOfTheLimit(int spent, int limit, int score) =>
        Assert.Equal(score, ReportCardMath.BudgetScore(spent, limit));

    [Theory]
    [InlineData(0, 1000, 100)]
    [InlineData(100, 1000, 100)]   // up to 10% of the income
    [InlineData(150, 1000, 85)]
    [InlineData(200, 1000, 70)]    // the threshold of warning rule 2
    [InlineData(300, 1000, 40)]
    [InlineData(500, 1000, 0)]
    public void UnnecessaryScore_LosesPointsAboveTenPercentOfTheIncome(int unnecessary, int income, int score) =>
        Assert.Equal(score, ReportCardMath.UnnecessaryScore(unnecessary, income));

    [Theory]
    [InlineData(0, 100, 100)]
    [InlineData(50, 100, 100)]
    [InlineData(100, 100, 100)]    // exactly the week's allowances is still within them
    [InlineData(105, 100, 90)]     // a near miss keeps most of the points
    [InlineData(110, 100, 80)]
    [InlineData(125, 100, 50)]
    [InlineData(150, 100, 0)]
    [InlineData(300, 100, 0)]
    [InlineData(0, 0, 100)]        // nothing to spend and nothing spent
    [InlineData(10, 0, 0)]         // nothing to spend, but spent anyway
    public void WeekScore_IsFullWithinTheAllowancesAndLosesTwoPointsPerPercentAbove(int spent, int budget, int score) =>
        Assert.Equal(score, ReportCardMath.WeekScore(spent, budget));

    [Fact]
    public void AllowanceScore_IsTheAverageOfTheWeeksAndNeedsTwoWeeks()
    {
        Assert.Equal(100m, ReportCardMath.AllowanceScore([new(100m, 20m), new(100m, 100m)]));
        Assert.Equal(70m, ReportCardMath.AllowanceScore(FourWeeks));
        Assert.Equal(0m, ReportCardMath.AllowanceScore([new(100m, 200m), new(0m, 5m)]));
        Assert.Null(ReportCardMath.AllowanceScore([new(100m, 20m)]));
        Assert.Null(ReportCardMath.AllowanceScore([]));
    }

    [Fact]
    public void WeekFact_IsKeptWhenTheSpendingIsNotAboveTheBudget()
    {
        Assert.True(new WeekFact(100m, 99.99m).IsKept);
        Assert.True(new WeekFact(100m, 100m).IsKept);
        Assert.False(new WeekFact(100m, 100.01m).IsKept);
    }

    // ---------- the overall grade ----------

    private static readonly BudgetFact CoffeeOver = new("Kafe/Kahve", 500m, 550m, true);   // 110% -> 55
    private static readonly BudgetFact MarketFine = new("Market", 1500m, 600m, false);      // 40%  -> 100

    /// <summary>Two weeks within the allowances, one 15% above (70) and one 45% above (10): 70 on average.</summary>
    private static readonly WeekFact[] FourWeeks = [new(100m, 50m), new(100m, 100m), new(100m, 115m), new(100m, 145m)];

    [Fact]
    public void Grade_IsTheWeightedAverageOfTheFourCourses()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [CoffeeOver, MarketFine], FourWeeks));

        // savings 80 x 35 + budgets 77,5 x 25 + unnecessary 70 x 20 + allowance 70 x 20
        Assert.Equal(75.375m, grading.Score);
        Assert.Equal(75, grading.Points);
        Assert.Equal("İyi", grading.Level);

        Assert.Equal([CourseKind.Savings, CourseKind.Budgets, CourseKind.Unnecessary, CourseKind.Allowance],
            grading.Courses.Select(c => c.Kind));
        Assert.Equal([80m, 77.5m, 70m, 70m], grading.Courses.Select(c => c.Score!.Value));
        Assert.Equal(["İyi", "İyi", "İyi", "İyi"], grading.Courses.Select(c => c.Level));
        Assert.Equal(["%10", "1/2 limit içinde", "%20", "2/4 hafta"], grading.Courses.Select(c => c.Value));
        Assert.Equal(100m, grading.Courses.Sum(c => c.Weight));
    }

    [Fact]
    public void Grade_GradesEveryLimitedCategoryOnItsOwnWorstFirst()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [MarketFine, CoffeeOver], FourWeeks));

        Assert.Equal(["Kafe/Kahve", "Market"], grading.Categories.Select(c => c.Category));
        Assert.Equal([110m, 40m], grading.Categories.Select(c => c.UsagePercent));
        Assert.Equal(["Orta", "Pekiyi"], grading.Categories.Select(c => c.Level));
        Assert.Equal([55, 100], grading.Categories.Select(c => c.Points));
    }

    [Fact]
    public void Grade_ALimitThatIsExactlyUsedStillCountsAsWithinTheLimit()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 0m, [new("Market", 500m, 500m, false)], FourWeeks));

        Assert.Equal("1/1 limit içinde", grading.Courses.Single(c => c.Kind == CourseKind.Budgets).Value);
    }

    [Fact]
    public void Grade_WithoutLimitsTheBudgetCourseIsLeftOutAndItsWeightShared()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [], FourWeeks));

        var budgets = grading.Courses.Single(c => c.Kind == CourseKind.Budgets);
        Assert.False(budgets.IsGraded);
        Assert.Null(budgets.Level);
        Assert.Contains("limit", budgets.NotGradedReason);
        Assert.Empty(grading.Categories);
        // (80 x 35 + 70 x 20 + 70 x 20) / 75
        Assert.Equal(74.67m, Math.Round(grading.Score, 2));
    }

    [Fact]
    public void Grade_WithTooFewWeeksTheAllowanceCourseIsLeftOut()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [CoffeeOver, MarketFine], [new(100m, 20m)]));

        var allowance = grading.Courses.Single(c => c.Kind == CourseKind.Allowance);
        Assert.False(allowance.IsGraded);
        Assert.Contains("1 hafta", allowance.NotGradedReason);
        Assert.Contains("en az 2 hafta", allowance.NotGradedReason);
        // (80 x 35 + 77,5 x 25 + 70 x 20) / 80
        Assert.Equal(76.71875m, grading.Score);
    }

    [Fact]
    public void Grade_WithOnlyTheTwoIncomeCoursesStillGivesAGrade()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [], []));

        // (80 x 35 + 70 x 20) / 55
        Assert.Equal(76.36m, Math.Round(grading.Score, 2));
        Assert.Equal("İyi", grading.Level);
    }

    [Fact]
    public void Grade_ShowsADeficitAsANegativePercent()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 1040m, 0m, [], FourWeeks));

        Assert.Equal("-%4", grading.Courses.Single(c => c.Kind == CourseKind.Savings).Value);
    }

    [Fact]
    public void Grade_RefusesAMonthWithoutIncome() =>
        Assert.Throws<ArgumentException>(() => ReportCardMath.Grade(new MonthFacts(0m, 100m, 0m, [], FourWeeks)));

    // ---------- a whole month from the database ----------

    /// <summary>
    /// 1.000 in; groceries 5 x 100, coffee 4 x 50 against a coffee limit of 200.
    /// Savings 30% -> 100, the limit exactly used -> 70, unnecessary 20% -> 70.
    /// September 2026 starts on a Tuesday, so its weeks are 1–6, 7–13, 14–20 and 21–30 (the last three days join
    /// the week before). With about 33 a day: week 1 spends 150 of 200; week 2 spends 300 of 247,87 (21% above,
    /// 58 points); week 3 spends 250 of 226,45 (10% above, 79 points); week 4 spends nothing. Allowance 84.
    /// Overall 83,4.
    /// </summary>
    private static void SeedStandardMonth(TestDb t)
    {
        AddIncome(t.Context, Alice, 1000m, September);
        Spend(t, September, 100m, Market, 3, 7, 11, 15, 19);
        Spend(t, September, 50m, Coffee, 4, 8, 12, 16);
        AddBudget(t.Context, Alice, Coffee, 200m);
    }

    [Fact]
    public async Task GetAsync_GradesAFinishedMonth()
    {
        using var t = new TestDb();
        SeedStandardMonth(t);

        var card = await Get(t);

        Assert.Equal(ReportCardStatus.Graded, card.Status);
        Assert.True(card.IsGraded);
        Assert.Null(card.Reason);
        Assert.Equal((2026, 9), (card.Year, card.Month));
        Assert.Equal(83, card.Points);
        Assert.Equal("İyi", card.Level);
        Assert.Equal([100, 70, 70, 84], card.Courses.Select(c => c.Points!.Value));
        Assert.Equal(["Pekiyi", "İyi", "İyi", "İyi"], card.Courses.Select(c => c.Level));
        Assert.Equal(["%30", "1/1 limit içinde", "%20", "2/4 hafta"], card.Courses.Select(c => c.Value));

        var coffee = Assert.Single(card.Categories);
        Assert.Equal(("Kafe/Kahve", 100m, 70, "İyi"), (coffee.Category, coffee.UsagePercent, coffee.Points, coffee.Level));
    }

    [Fact]
    public async Task GetAsync_ALimitWithoutSpendingGetsFullPointsAndGeneratedExpensesCountTowardsALimit()
    {
        using var t = new TestDb();
        SeedStandardMonth(t);
        AddBudget(t.Context, Alice, Entertainment, 400m);                    // nothing spent
        AddBudget(t.Context, Alice, Bills, 300m);
        AddGeneratedExpense(t.Context, Alice, 150m, Sep(10), Bills);          // from a recurring definition
        AddExpense(t.Context, Alice, 120m, Sep(20), Bills);                   // by hand: 270 of 300 = 90%

        var card = await Get(t);

        Assert.Equal(["Kafe/Kahve", "Faturalar", "Eğlence"], card.Categories.Select(c => c.Category));
        Assert.Equal([100m, 90m, 0m], card.Categories.Select(c => c.UsagePercent));
        Assert.Equal([70, 85, 100], card.Categories.Select(c => c.Points));
        Assert.Equal(["İyi", "Pekiyi", "Pekiyi"], card.Categories.Select(c => c.Level));
        Assert.Equal("3/3 limit içinde", Course(card, CourseKind.Budgets).Value);
    }

    // ---------- months that get no grade ----------

    [Fact]
    public async Task GetAsync_GivesNoGradeWithoutIncomeAndSaysWhy()
    {
        using var t = new TestDb();
        Spend(t, September, 50m, Market, 3, 7, 11, 15, 19);

        var card = await Get(t);

        Assert.Equal(ReportCardStatus.NoIncome, card.Status);
        Assert.False(card.IsGraded);
        Assert.Null(card.Level);
        Assert.Null(card.Points);
        Assert.Contains("gelir", card.Reason);
        Assert.Empty(card.Courses);
        Assert.Empty(card.Suggestions);
        Assert.Null(card.Comparison);
    }

    [Fact]
    public async Task GetAsync_GivesNoGradeWhenExpensesAreOnFewerThanFiveDays()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, September);
        Spend(t, September, 50m, Market, 3, 7, 11, 15);
        AddExpense(t.Context, Alice, 10m, Sep(15), Coffee); // a second expense on the same day is not a fifth day

        var card = await Get(t);

        Assert.Equal(ReportCardStatus.TooLittleData, card.Status);
        Assert.Contains("4 günde", card.Reason);
        Assert.Contains("en az 5 gün", card.Reason);
    }

    [Fact]
    public async Task GetAsync_FiveExpenseDaysAreEnough()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 20m);

        Assert.Equal(ReportCardStatus.Graded, (await Get(t)).Status);
    }

    [Theory]
    [InlineData(2026, 10)] // the current month
    [InlineData(2026, 11)] // a future month
    public async Task GetAsync_GivesNoGradeBeforeTheMonthIsOver(int year, int month)
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 20m);
        SimpleMonth(t, new DateTime(2026, 10, 1), 20m);

        var card = await Get(t, new DateTime(year, month, 1));

        Assert.Equal(ReportCardStatus.NotFinished, card.Status);
        Assert.Contains("henüz bitmedi", card.Reason);
    }

    [Fact]
    public async Task GetAsync_TheMonthIsGradedFromTheFirstDayOfTheNextOne()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 20m);

        Assert.Equal(ReportCardStatus.NotFinished, (await Get(t, today: Sep(30))).Status);
        Assert.Equal(ReportCardStatus.Graded, (await Get(t, today: new DateTime(2026, 10, 1))).Status);
    }

    [Fact]
    public async Task GetAsync_GivesNoGradeForAMonthWithoutTransactions()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 20m);

        Assert.Equal(ReportCardStatus.NoData, (await Get(t)).Status);          // September is empty
        Assert.Equal(ReportCardStatus.NoData, (await Get(t, user: Bob)).Status); // a user without any data
    }

    // ---------- the allowance course: week by week, rebuilt from the transactions ----------
    // September 2026 starts on a Tuesday: its weeks are 1–6, 7–13, 14–20 and 21–30.

    [Fact]
    public async Task GetAsync_CountsTheWeeksTheSpendingStayedWithinTheDailyAllowances()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15, 19); // about 100 a day is available

        var allowance = Course(await Get(t), CourseKind.Allowance);

        Assert.Equal("4/4 hafta", allowance.Value);
        Assert.Equal(100m, allowance.Score);
    }

    [Fact]
    public async Task GetAsync_AnExpensiveDayIsFineWhenTheWeekEvensItOut()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 15, 19, 23);
        // four times the allowance of that day (about 124), but far below the week's 869
        AddExpense(t.Context, Alice, 500m, Sep(8), Entertainment);

        var allowance = Course(await Get(t), CourseKind.Allowance);

        Assert.Equal("4/4 hafta", allowance.Value);
        Assert.Equal(100m, allowance.Score);
    }

    [Fact]
    public async Task GetAsync_AWeekAboveItsAllowancesIsNotKeptAndLosesPointsByHowFarItWentOver()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 15, 19, 23);
        // week 2 starts with 2.980 for 24 days: 7 x 124,16 = 869,12. Spending 1.000 is 15% above -> 70 points.
        AddExpense(t.Context, Alice, 500m, Sep(8), Entertainment);
        AddExpense(t.Context, Alice, 500m, Sep(10), Entertainment);

        var allowance = Course(await Get(t), CourseKind.Allowance);

        Assert.Equal("3/4 hafta", allowance.Value);
        Assert.Equal(92, allowance.Points); // (100 + 70 + 100 + 100) / 4
    }

    [Fact]
    public async Task GetAsync_AFixedExpenseIsNotSpendingAgainstTheAllowance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15, 19);
        AddExpense(t.Context, Alice, 2000m, Sep(2), Rent); // comes off the pool, the weeks still fit

        Assert.Equal("4/4 hafta", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_TheSameAmountAsVariableSpendingBreaksTheWeek()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15, 19);
        AddExpense(t.Context, Alice, 2000m, Sep(2), Entertainment); // week 1 has 6 x 100 to spend

        var allowance = Course(await Get(t), CourseKind.Allowance);

        Assert.Equal("3/4 hafta", allowance.Value);
        Assert.Equal(75m, allowance.Score); // the broken week is far above its allowances: no points for it
    }

    [Fact]
    public async Task GetAsync_TheAllowanceRunsToTheNextRecurringIncomeOfTheMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, September);
        Spend(t, September, 1m, Market, 2, 3, 4, 5);
        AddGeneratedIncome(t, 1000m, Sep(20));
        // week 2 starts with 296 for the 13 days until the 20th: 7 x 22,76 = 159 (until the month end it would be 86)
        AddExpense(t.Context, Alice, 110m, Sep(10), Market);

        Assert.Equal("4/4 hafta", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_RecurringExpensesBeforeTheNextIncomeAreSetAside()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, September);
        Spend(t, September, 1m, Market, 2, 3, 4, 5);
        AddGeneratedIncome(t, 1000m, Sep(20));
        AddGeneratedExpense(t.Context, Alice, 100m, Sep(15), Bills);
        // only 196 is free for the 13 days: 7 x 15,07 = 105, so 110 is above it
        AddExpense(t.Context, Alice, 110m, Sep(10), Market);

        Assert.Equal("3/4 hafta", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_TheAllowanceUsesTheAllTimeBalance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, August);
        AddExpense(t.Context, Alice, 1500m, new DateTime(2026, 8, 5), Market); // August ends 500 short
        AddIncome(t.Context, Alice, 400m, September);
        Spend(t, September, 10m, Market, 3, 7, 11, 15, 19);

        var allowance = Course(await Get(t), CourseKind.Allowance);

        // September never gets above zero: every week with spending is above an allowance of nothing.
        Assert.Equal("1/4 hafta", allowance.Value);
        Assert.Equal(25m, allowance.Score);
    }

    [Fact]
    public async Task GetAsync_MoneyPutIntoAGoalIsNotAvailableForTheAllowance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, September);
        Spend(t, September, 5m, Market, 3, 4, 5, 6, 7);
        Assert.Equal("4/4 hafta", Course(await Get(t), CourseKind.Allowance).Value);

        var goal = AddGoal(t.Context, Alice, 4500m);
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 250m, Date = Sep(2), Source = GoalContributionSource.Manual
        });
        t.Context.SaveChanges();

        // after the 250 only about 1,40 a day is left in week 1, and 20 was spent
        Assert.Equal("3/4 hafta", Course(await Get(t), CourseKind.Allowance).Value);
    }

    // ---------- how a month is cut into weeks ----------

    [Theory]
    [InlineData(2027, 2, "4/4 hafta")] // starts on a Monday, 28 days: four whole weeks
    [InlineData(2026, 7, "5/5 hafta")] // Wednesday: 1–5 and 27–31 are five days each and stand on their own
    [InlineData(2026, 1, "5/5 hafta")] // Thursday: the first four days are just enough to be a week
    [InlineData(2026, 9, "4/4 hafta")] // Tuesday: the last three days (28–30) join the week before
    [InlineData(2026, 8, "4/4 hafta")] // Saturday: 1–2 join the first week and the 31st joins the last one
    public async Task GetAsync_ShortPiecesAtTheEndsOfTheMonthJoinTheNeighbouringWeek(int year, int month, string expected)
    {
        using var t = new TestDb();
        var period = new DateTime(year, month, 1);
        SimpleMonth(t, period, 20m);

        var card = await Get(t, period, today: period.AddMonths(1).AddDays(5));

        Assert.Equal(expected, Course(card, CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_AShortPieceIsJudgedTogetherWithItsNeighbour()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15);
        // The 29th alone would be far above its own allowance; together with 21–27 the ten days have about 2.900.
        AddExpense(t.Context, Alice, 900m, Sep(29), Entertainment);

        Assert.Equal("4/4 hafta", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_AUserWhoStartedMidMonthIsCountedFromTheFirstTransaction()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Sep(16)); // a Wednesday: 16–20, then 21–30
        Spend(t, September, 10m, Market, 17, 19, 21, 23, 25);

        Assert.Equal("2/2 hafta", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_WithOnlyOneWeekTheAllowanceCourseIsNotGradedButTheMonthIs()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Sep(25)); // 25–27 are too short on their own and join 28–30
        Spend(t, September, 10m, Market, 26, 27, 28, 29, 30);

        var card = await Get(t);

        var allowance = Course(card, CourseKind.Allowance);
        Assert.False(allowance.IsGraded);
        Assert.Contains("1 hafta", allowance.NotGradedReason);
        Assert.Equal(ReportCardStatus.Graded, card.Status);
        Assert.Equal((100, "Pekiyi"), (card.Points, card.Level)); // savings 95% and no unnecessary spending
    }

    // ---------- comparison with the month before ----------
    // 210 a day in SimpleMonth is a deficit (savings 45) with most weeks above their allowances; 20 a day is 100 points.

    [Fact]
    public async Task GetAsync_SaysHowFarTheScoreWentUp()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 210m);
        SimpleMonth(t, September, 20m);

        var card = await Get(t);

        Assert.Equal((55, "Orta"), ((await Get(t, August)).Points, (await Get(t, August)).Level));
        Assert.Equal((100, "Pekiyi"), (card.Points, card.Level));
        Assert.NotNull(card.Comparison);
        Assert.Equal((2026, 8, "Orta", 55, 45), (card.Comparison.Year, card.Comparison.Month, card.Comparison.Level,
            card.Comparison.Points, card.Comparison.Change));
        Assert.Equal("Ağustos'ta 55 puan almıştın (Orta), Eylül'de 100 puan aldın (Pekiyi): 45 puan yükseldin.", card.Comparison.Text);
        Assert.Equal(1, Course(card, CourseKind.Savings).Trend);
        Assert.Equal(0, Course(card, CourseKind.Unnecessary).Trend);
        Assert.Equal(1, Course(card, CourseKind.Allowance).Trend);
        Assert.Null(Course(card, CourseKind.Budgets).Trend); // not graded in either month
    }

    [Fact]
    public async Task GetAsync_SaysHowFarTheScoreWentDown()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 20m);
        SimpleMonth(t, September, 210m); // the 900 left from August keeps every week within its allowances

        var card = await Get(t);

        Assert.Equal((74, "İyi"), (card.Points, card.Level)); // savings 45, the other two courses 100
        Assert.Equal(-26, card.Comparison!.Change);
        Assert.Equal("Ağustos'ta 100 puan almıştın (Pekiyi), Eylül'de 74 puan aldın (İyi): 26 puan düştün.", card.Comparison.Text);
        Assert.Equal(-1, Course(card, CourseKind.Savings).Trend);
        Assert.Equal(0, Course(card, CourseKind.Allowance).Trend);
    }

    [Fact]
    public async Task GetAsync_SaysWhenTheScoreStayedTheSame()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 20m);
        SimpleMonth(t, September, 20m);

        var card = await Get(t);

        Assert.Equal(0, card.Comparison!.Change);
        Assert.Equal("Ağustos'ta da 100 puan almıştın (Pekiyi); Eylül'de puanın aynı kaldı.", card.Comparison.Text);
    }

    [Fact]
    public async Task GetAsync_ComparesWithTheNearestEarlierMonthThatHasAGrade()
    {
        using var t = new TestDb();
        SimpleMonth(t, July, 210m);
        Spend(t, August, 10m, Market, 3, 7, 11, 15, 19); // no income: August gets no grade
        SimpleMonth(t, September, 20m);

        var card = await Get(t);

        Assert.Equal(ReportCardStatus.NoIncome, (await Get(t, August)).Status);
        Assert.Equal((2026, 7), (card.Comparison!.Year, card.Comparison.Month));
        Assert.StartsWith("Temmuz'da 60 puan almıştın (Orta)", card.Comparison.Text);
    }

    [Fact]
    public async Task GetAsync_NamesTheYearWhenTheComparedMonthIsInAnotherOne()
    {
        using var t = new TestDb();
        SimpleMonth(t, new DateTime(2025, 12, 1), 210m);
        SimpleMonth(t, new DateTime(2026, 1, 1), 20m);

        var card = await Get(t, new DateTime(2026, 1, 1), today: new DateTime(2026, 2, 10));

        Assert.Equal("Aralık'ta (2025) 58 puan almıştın (Orta), Ocak'ta 100 puan aldın (Pekiyi): 42 puan yükseldin.", card.Comparison!.Text);
    }

    [Fact]
    public async Task GetAsync_TheFirstCardHasNothingToCompareWith()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 20m);

        var card = await Get(t);

        Assert.Null(card.Comparison);
        Assert.All(card.Courses, c => Assert.Null(c.Trend));
    }

    // ---------- suggestions ----------

    [Fact]
    public async Task GetAsync_TheTopLevelNeedsNoSuggestion()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 20m);

        Assert.Empty((await Get(t)).Suggestions);
    }

    [Fact]
    public async Task GetAsync_SuggestsTheTwoChangesThatWouldHaveRaisedTheScoreMost()
    {
        using var t = new TestDb();
        // 1.000 in; coffee 5 x 60 against a limit of 200 (150% -> 0), groceries 5 x 130.
        // Savings 5% -> 70, unnecessary 30% -> 40, two of the four weeks far above their allowances -> 50:
        // overall 42,5, shown as 43.
        AddIncome(t.Context, Alice, 1000m, September);
        Spend(t, September, 60m, Coffee, 3, 7, 11, 15, 19);
        Spend(t, September, 130m, Market, 4, 8, 12, 16, 20);
        AddBudget(t.Context, Alice, Coffee, 200m);

        var card = await Get(t);

        Assert.Equal((43, "Zayıf"), (card.Points, card.Level));
        Assert.Equal("2/4 hafta", Course(card, CourseKind.Allowance).Value);
        Assert.Equal(2, card.Suggestions.Count);
        // coffee at its limit: savings 15% -> 90, unnecessary 20% -> 70, the limit -> 70: overall 73
        Assert.Equal("Kafe/Kahve limitini 100,00 TL aştın. Limitte kalsaydın genel puanın 43 yerine 73, seviyen Zayıf yerine İyi olurdu.",
            card.Suggestions[0].Text);
        Assert.Equal(("İyi", 73), (card.Suggestions[0].Level, card.Suggestions[0].Points));
        // unnecessary spending at 20%: the limit is still broken, overall 55,5
        Assert.Equal("Gereksiz harcama payın %30. 100,00 TL daha az harcasaydın %20'ye inerdi; genel puanın 43 yerine 56, seviyen Zayıf yerine Orta olurdu.",
            card.Suggestions[1].Text);
        Assert.Equal(("Orta", 56), (card.Suggestions[1].Level, card.Suggestions[1].Points));
    }

    [Fact]
    public async Task GetAsync_SuggestsSavingEnoughForARounderSavingsScore()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 190m); // savings 5% -> 70, two of four weeks kept -> 50: overall 72,67

        var card = await Get(t);

        Assert.Equal((73, "İyi"), (card.Points, card.Level));
        var suggestion = Assert.Single(card.Suggestions);
        // 7,5% saved is 75 points: 25 more than the 50 that was left; overall 75
        Assert.Equal("Tasarruf puanın 70. 25,00 TL daha az harcasaydın 75 olurdu; genel puanın 73 yerine 75 olurdu.",
            suggestion.Text);
        Assert.Equal(("İyi", 75), (suggestion.Level, suggestion.Points));
    }

    [Fact]
    public async Task GetAsync_GoesOnToTheNextSavingsStepWhenTheNearestOneChangesNothing()
    {
        using var t = new TestDb();
        // 1.000 in, 928 out: savings 7,2% -> 74,4, two of four weeks kept -> 50: overall 74,72, shown as 75.
        // 75 points in savings (3 less) gives exactly 75, still the same; 80 points (28 less) gives 77,33.
        SimpleMonth(t, September, 185.6m);

        var card = await Get(t);

        Assert.Equal((75, "İyi"), (card.Points, card.Level));
        var suggestion = Assert.Single(card.Suggestions);
        Assert.Equal("Tasarruf puanın 74. 28,00 TL daha az harcasaydın 80 olurdu; genel puanın 75 yerine 77 olurdu.",
            suggestion.Text);
    }

    // ---------- the list of months ----------

    [Fact]
    public async Task GetHistoryAsync_ListsTheFinishedMonthsNewestFirst()
    {
        using var t = new TestDb();
        SimpleMonth(t, July, 210m);
        Spend(t, August, 10m, Market, 3, 7, 11, 15, 19);
        SimpleMonth(t, September, 20m);
        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1)); // the current month is not listed

        var history = await new ReportCardService(t.Context).GetHistoryAsync(Alice, Today);

        Assert.Equal([(2026, 9), (2026, 8), (2026, 7)], history.Select(h => (h.Year, h.Month)));
        Assert.Equal(["Pekiyi", null, "Orta"], history.Select(h => h.Level));
        Assert.Equal([100, null, 60], history.Select(h => h.Points));
        Assert.Equal(ReportCardStatus.NoIncome, history[1].Status);
        Assert.Contains("gelir", history[1].Reason);
        Assert.Null(history[0].Reason);
    }

    [Fact]
    public async Task GetHistoryAsync_IncludesEmptyMonthsBetweenButNothingBeforeTheFirstTransaction()
    {
        using var t = new TestDb();
        SimpleMonth(t, July, 20m);
        SimpleMonth(t, September, 20m);

        var history = await new ReportCardService(t.Context).GetHistoryAsync(Alice, Today);

        Assert.Equal([(2026, 9), (2026, 8), (2026, 7)], history.Select(h => (h.Year, h.Month)));
        Assert.Equal(ReportCardStatus.NoData, history[1].Status);
    }

    [Fact]
    public async Task GetHistoryAsync_StopsAtTheRequestedNumberOfMonths()
    {
        using var t = new TestDb();
        SimpleMonth(t, July, 20m);
        SimpleMonth(t, August, 20m);
        SimpleMonth(t, September, 20m);

        var history = await new ReportCardService(t.Context).GetHistoryAsync(Alice, Today, months: 2);

        Assert.Equal([(2026, 9), (2026, 8)], history.Select(h => (h.Year, h.Month)));
    }

    [Fact]
    public async Task GetHistoryAsync_IsEmptyWithoutFinishedMonths()
    {
        using var t = new TestDb();
        Assert.Empty(await new ReportCardService(t.Context).GetHistoryAsync(Alice, Today));

        AddIncome(t.Context, Alice, 1000m, new DateTime(2026, 10, 1)); // only the current month
        Assert.Empty(await new ReportCardService(t.Context).GetHistoryAsync(Alice, Today));
    }

    // ---------- whose data ----------

    [Fact]
    public async Task GetAsync_OnlyUsesTheUsersOwnData()
    {
        using var t = new TestDb();
        SeedStandardMonth(t);
        var before = await Get(t);

        Add(t.Context, Bob, TransactionType.Income, 9000m, September, TestData.Income);
        foreach (var day in new[] { 2, 6, 10, 14, 18 })
            AddExpense(t.Context, Bob, 900m, Sep(day), Coffee);
        AddBudget(t.Context, Bob, Market, 50m);
        var bobsGoal = AddGoal(t.Context, Bob, 1000m);
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = bobsGoal.Id, Amount = 900m, Date = Sep(2), Source = GoalContributionSource.Manual
        });
        t.Context.SaveChanges();

        var after = await Get(t);

        Assert.Equal(before.Score, after.Score);
        Assert.Equal(before.Courses.Select(c => c.Value), after.Courses.Select(c => c.Value));
        Assert.Equal(["Kafe/Kahve"], after.Categories.Select(c => c.Category));

        var history = await new ReportCardService(t.Context).GetHistoryAsync(Bob, Today);
        Assert.Equal((2026, 9), (history.Single().Year, history.Single().Month));
    }
}
