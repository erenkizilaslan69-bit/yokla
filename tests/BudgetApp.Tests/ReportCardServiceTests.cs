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

    // ---------- the letter scale ----------

    [Theory]
    [InlineData(100, "AA")]
    [InlineData(90, "AA")]
    [InlineData(89, "BA")]
    [InlineData(85, "BA")]
    [InlineData(84, "BB")]
    [InlineData(80, "BB")]
    [InlineData(79, "CB")]
    [InlineData(75, "CB")]
    [InlineData(74, "CC")]
    [InlineData(70, "CC")]
    [InlineData(69, "DC")]
    [InlineData(65, "DC")]
    [InlineData(64, "DD")]
    [InlineData(60, "DD")]
    [InlineData(59, "FD")]
    [InlineData(50, "FD")]
    [InlineData(49, "FF")]
    [InlineData(0, "FF")]
    public void LetterFor_FollowsTheUniversityScale(int score, string letter) =>
        Assert.Equal(letter, GradeScale.LetterFor(score));

    [Theory]
    [InlineData("89.5", "AA")]   // the letter comes from the rounded score, the one that is shown
    [InlineData("89.49", "BA")]
    [InlineData("49.5", "FD")]
    [InlineData("49.4", "FF")]
    [InlineData("-5", "FF")]
    [InlineData("120", "AA")]
    public void LetterFor_RoundsBeforeChoosingTheLetter(string score, string letter) =>
        Assert.Equal(letter, GradeScale.LetterFor(decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("89.5", 90)]
    [InlineData("89.49", 89)]
    [InlineData("80.5", 81)]
    [InlineData("-3", 0)]
    [InlineData("140", 100)]
    public void Points_RoundsHalvesUpAndStaysBetweenZeroAndAHundred(string score, int points) =>
        Assert.Equal(points, GradeScale.Points(decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Rank_OrdersTheLettersFromFfToAa()
    {
        Assert.Equal(0, GradeScale.Rank("FF"));
        Assert.Equal(1, GradeScale.Rank("FD"));
        Assert.Equal(6, GradeScale.Rank("BB"));
        Assert.Equal(8, GradeScale.Rank("AA"));
        Assert.Throws<ArgumentException>(() => GradeScale.Rank("AB"));
    }

    [Theory]
    [InlineData("95", null)]
    [InlineData("90", null)]
    [InlineData("84.6", 90)]  // shown as 85 (BA); the next letter starts at 90
    [InlineData("67.8", 70)]
    [InlineData("50", 60)]
    [InlineData("49", 50)]
    [InlineData("0", 50)]
    public void NextStepPoints_IsTheStartOfTheNextBetterLetter(string score, int? next) =>
        Assert.Equal(next, GradeScale.NextStepPoints(decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

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
    [InlineData(570, 500, 49)]     // 114%: the first FF
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

    [Fact]
    public void AllowanceScore_IsTheShareOfDaysKeptAndNeedsTenDays()
    {
        Assert.Equal(100m, ReportCardMath.AllowanceScore(10, 10));
        Assert.Equal(50m, ReportCardMath.AllowanceScore(15, 30));
        Assert.Equal(0m, ReportCardMath.AllowanceScore(0, 30));
        Assert.Null(ReportCardMath.AllowanceScore(9, 9));
    }

    // ---------- the overall grade ----------

    private static readonly BudgetFact CoffeeOver = new("Kafe/Kahve", 500m, 550m, true);   // 110% -> 55
    private static readonly BudgetFact MarketFine = new("Market", 1500m, 600m, false);      // 40%  -> 100

    [Fact]
    public void Grade_IsTheWeightedAverageOfTheFourCourses()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [CoffeeOver, MarketFine], 30, 21));

        // savings 80 x 35 + budgets 77,5 x 25 + unnecessary 70 x 20 + allowance 70 x 20
        Assert.Equal(75.375m, grading.Score);
        Assert.Equal(75, grading.Points);
        Assert.Equal("CB", grading.Letter);

        Assert.Equal([CourseKind.Savings, CourseKind.Budgets, CourseKind.Unnecessary, CourseKind.Allowance],
            grading.Courses.Select(c => c.Kind));
        Assert.Equal([80m, 77.5m, 70m, 70m], grading.Courses.Select(c => c.Score!.Value));
        Assert.Equal(["BB", "CB", "CC", "CC"], grading.Courses.Select(c => c.Letter));
        Assert.Equal(["%10", "1/2 limit içinde", "%20", "21/30 gün"], grading.Courses.Select(c => c.Value));
        Assert.Equal(100m, grading.Courses.Sum(c => c.Weight));
    }

    [Fact]
    public void Grade_GradesEveryLimitedCategoryOnItsOwnWorstFirst()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [MarketFine, CoffeeOver], 30, 21));

        Assert.Equal(["Kafe/Kahve", "Market"], grading.Categories.Select(c => c.Category));
        Assert.Equal([110m, 40m], grading.Categories.Select(c => c.UsagePercent));
        Assert.Equal(["FD", "AA"], grading.Categories.Select(c => c.Letter));
        Assert.Equal([55, 100], grading.Categories.Select(c => c.Points));
    }

    [Fact]
    public void Grade_ALimitThatIsExactlyUsedStillCountsAsWithinTheLimit()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 0m, [new("Market", 500m, 500m, false)], 30, 30));

        Assert.Equal("1/1 limit içinde", grading.Courses.Single(c => c.Kind == CourseKind.Budgets).Value);
    }

    [Fact]
    public void Grade_WithoutLimitsTheBudgetCourseIsLeftOutAndItsWeightShared()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [], 30, 21));

        var budgets = grading.Courses.Single(c => c.Kind == CourseKind.Budgets);
        Assert.False(budgets.IsGraded);
        Assert.Null(budgets.Letter);
        Assert.Contains("limit", budgets.NotGradedReason);
        Assert.Empty(grading.Categories);
        // (80 x 35 + 70 x 20 + 70 x 20) / 75
        Assert.Equal(74.67m, Math.Round(grading.Score, 2));
    }

    [Fact]
    public void Grade_WithTooFewAllowanceDaysThatCourseIsLeftOut()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [CoffeeOver, MarketFine], 6, 6));

        var allowance = grading.Courses.Single(c => c.Kind == CourseKind.Allowance);
        Assert.False(allowance.IsGraded);
        Assert.Contains("6 gün", allowance.NotGradedReason);
        // (80 x 35 + 77,5 x 25 + 70 x 20) / 80
        Assert.Equal(76.71875m, grading.Score);
    }

    [Fact]
    public void Grade_WithOnlyTheTwoIncomeCoursesStillGivesAGrade()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 900m, 200m, [], 0, 0));

        // (80 x 35 + 70 x 20) / 55
        Assert.Equal(76.36m, Math.Round(grading.Score, 2));
        Assert.Equal("CB", grading.Letter);
    }

    [Fact]
    public void Grade_ShowsADeficitAsANegativePercent()
    {
        var grading = ReportCardMath.Grade(new MonthFacts(1000m, 1040m, 0m, [], 30, 30));

        Assert.Equal("-%4", grading.Courses.Single(c => c.Kind == CourseKind.Savings).Value);
    }

    [Fact]
    public void Grade_RefusesAMonthWithoutIncome() =>
        Assert.Throws<ArgumentException>(() => ReportCardMath.Grade(new MonthFacts(0m, 100m, 0m, [], 30, 30)));

    // ---------- a whole month from the database ----------

    /// <summary>
    /// 1.000 in; groceries 5 x 100, coffee 4 x 50 against a coffee limit of 200.
    /// Savings 30% -> 100, the limit exactly used -> 70, unnecessary 20% -> 70, and every one of the nine spending
    /// days is above the allowance of about 33 a day -> 21/30 = 70. Overall 80,5, shown as 81.
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
        Assert.Equal(80.5m, card.Score);
        Assert.Equal(81, card.Points);
        Assert.Equal("BB", card.Letter);
        Assert.Equal([100m, 70m, 70m, 70m], card.Courses.Select(c => c.Score!.Value));
        Assert.Equal(["%30", "1/1 limit içinde", "%20", "21/30 gün"], card.Courses.Select(c => c.Value));

        var coffee = Assert.Single(card.Categories);
        Assert.Equal(("Kafe/Kahve", 100m, "CC"), (coffee.Category, coffee.UsagePercent, coffee.Letter));
    }

    [Fact]
    public async Task GetAsync_ALimitWithoutSpendingIsAnAaAndGeneratedExpensesCountTowardsALimit()
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
        Assert.Equal(["CC", "BA", "AA"], card.Categories.Select(c => c.Letter));
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
        Assert.Null(card.Letter);
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

    // ---------- the allowance course, rebuilt from the transactions ----------

    [Fact]
    public async Task GetAsync_CountsTheDaysTheSpendingStayedWithinTheAllowance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15, 19); // about 100 a day is available

        var allowance = Course(await Get(t), CourseKind.Allowance);

        Assert.Equal("30/30 gün", allowance.Value);
        Assert.Equal(100m, allowance.Score);
    }

    [Fact]
    public async Task GetAsync_ADayAboveTheAllowanceIsNotKept()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15, 19);
        AddExpense(t.Context, Alice, 2000m, Sep(2), Entertainment); // 3.000 / 29 days is about 103

        Assert.Equal("29/30 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_AFixedExpenseIsNotADayAboveTheAllowance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 3000m, September);
        Spend(t, September, 20m, Market, 3, 7, 11, 15, 19);
        AddExpense(t.Context, Alice, 2000m, Sep(2), Rent); // comes off the pool: the later days still fit (1.000 / 28)

        Assert.Equal("30/30 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_TheAllowanceRunsToTheNextRecurringIncomeOfTheMonth()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, September);
        Spend(t, September, 1m, Market, 2, 3, 4, 5);
        AddGeneratedIncome(t, 1000m, Sep(20));
        // 296 on hand for the 10 days until the 20th = 29,60 (until the month end it would only be 14,09)
        AddExpense(t.Context, Alice, 25m, Sep(10), Market);

        Assert.Equal("30/30 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_RecurringExpensesBeforeTheNextIncomeAreSetAside()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, September);
        Spend(t, September, 1m, Market, 2, 3, 4, 5);
        AddGeneratedIncome(t, 1000m, Sep(20));
        AddGeneratedExpense(t.Context, Alice, 100m, Sep(15), Bills);
        // only 196 is free for the 10 days = 19,60, so 25 is above it
        AddExpense(t.Context, Alice, 25m, Sep(10), Market);

        Assert.Equal("29/30 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_TheAllowanceUsesTheAllTimeBalance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, August);
        AddExpense(t.Context, Alice, 1500m, new DateTime(2026, 8, 5), Market); // August ends 500 short
        AddIncome(t.Context, Alice, 400m, September);
        Spend(t, September, 10m, Market, 3, 7, 11, 15, 19);

        // September never gets above zero, so every spending day is above an allowance of nothing.
        Assert.Equal("25/30 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_MoneyPutIntoAGoalIsNotAvailableForTheAllowance()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 300m, September);
        Spend(t, September, 5m, Market, 3, 4, 5, 6, 7);
        Assert.Equal("30/30 gün", Course(await Get(t), CourseKind.Allowance).Value);

        var goal = AddGoal(t.Context, Alice, 4500m);
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 250m, Date = Sep(2), Source = GoalContributionSource.Manual
        });
        t.Context.SaveChanges();

        // 50 left for 28 days is less than 2 a day
        Assert.Equal("25/30 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_AUserWhoStartedMidMonthIsCountedFromTheFirstTransaction()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Sep(16));
        Spend(t, September, 10m, Market, 17, 19, 21, 23, 25);

        Assert.Equal("15/15 gün", Course(await Get(t), CourseKind.Allowance).Value);
    }

    [Fact]
    public async Task GetAsync_WithFewerThanTenDaysTheAllowanceCourseIsNotGradedButTheMonthIs()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 1000m, Sep(25));
        Spend(t, September, 10m, Market, 26, 27, 28, 29, 30);

        var card = await Get(t);

        var allowance = Course(card, CourseKind.Allowance);
        Assert.False(allowance.IsGraded);
        Assert.Contains("6 gün", allowance.NotGradedReason);
        Assert.Equal(ReportCardStatus.Graded, card.Status);
        Assert.Equal("AA", card.Letter); // savings 95% and no unnecessary spending
    }

    // ---------- comparison with the month before ----------

    [Fact]
    public async Task GetAsync_SaysHowFarTheGradeWentUp()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 210m);
        SimpleMonth(t, September, 20m);

        var card = await Get(t);

        Assert.Equal("CC", (await Get(t, August)).Letter);
        Assert.Equal("AA", card.Letter);
        Assert.NotNull(card.Comparison);
        Assert.Equal((2026, 8, "CC", 70, 4), (card.Comparison.Year, card.Comparison.Month, card.Comparison.Letter,
            card.Comparison.Points, card.Comparison.Steps));
        Assert.Equal("Ağustos'ta CC almıştın, Eylül'de AA aldın: 4 basamak yükseldin.", card.Comparison.Text);
        Assert.Equal(1, Course(card, CourseKind.Savings).Trend);
        Assert.Equal(0, Course(card, CourseKind.Unnecessary).Trend);
        Assert.Equal(1, Course(card, CourseKind.Allowance).Trend);
        Assert.Null(Course(card, CourseKind.Budgets).Trend); // not graded in either month
    }

    [Fact]
    public async Task GetAsync_SaysHowFarTheGradeWentDown()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 20m);
        SimpleMonth(t, September, 210m);

        var card = await Get(t);

        Assert.Equal(-4, card.Comparison!.Steps);
        Assert.Equal("Ağustos'ta AA almıştın, Eylül'de CC aldın: 4 basamak düştün.", card.Comparison.Text);
        Assert.Equal(-1, Course(card, CourseKind.Savings).Trend);
    }

    [Fact]
    public async Task GetAsync_SaysWhenTheGradeStayedTheSame()
    {
        using var t = new TestDb();
        SimpleMonth(t, August, 20m);
        SimpleMonth(t, September, 20m);

        var card = await Get(t);

        Assert.Equal(0, card.Comparison!.Steps);
        Assert.Equal("Ağustos'ta da AA almıştın; Eylül'de notun aynı kaldı.", card.Comparison.Text);
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
        Assert.StartsWith("Temmuz'da CC almıştın", card.Comparison.Text);
    }

    [Fact]
    public async Task GetAsync_NamesTheYearWhenTheComparedMonthIsInAnotherOne()
    {
        using var t = new TestDb();
        SimpleMonth(t, new DateTime(2025, 12, 1), 210m);
        SimpleMonth(t, new DateTime(2026, 1, 1), 20m);

        var card = await Get(t, new DateTime(2026, 1, 1), today: new DateTime(2026, 2, 10));

        Assert.Equal("Aralık'ta (2025) CC almıştın, Ocak'ta AA aldın: 4 basamak yükseldin.", card.Comparison!.Text);
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
    public async Task GetAsync_AnAaNeedsNoSuggestion()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 20m);

        Assert.Empty((await Get(t)).Suggestions);
    }

    [Fact]
    public async Task GetAsync_SuggestsTheTwoChangesThatWouldHaveRaisedTheGradeMost()
    {
        using var t = new TestDb();
        // 1.000 in; coffee 5 x 60 against a limit of 200 (150% -> 0), groceries 5 x 130.
        // Savings 5% -> 70, unnecessary 30% -> 40, allowance 20/30 -> 66,67: overall 45,83 = FF.
        AddIncome(t.Context, Alice, 1000m, September);
        Spend(t, September, 60m, Coffee, 3, 7, 11, 15, 19);
        Spend(t, September, 130m, Market, 4, 8, 12, 16, 20);
        AddBudget(t.Context, Alice, Coffee, 200m);

        var card = await Get(t);

        Assert.Equal((46, "FF"), (card.Points, card.Letter));
        Assert.Equal(2, card.Suggestions.Count);
        // coffee at its limit: savings 15% -> 90, unnecessary 20% -> 70, the limit -> 70: overall 76,33
        Assert.Equal("Kafe/Kahve limitini 100,00 TL aştın. Limitte kalsaydın genel notun FF yerine CB olurdu.",
            card.Suggestions[0].Text);
        Assert.Equal(("CB", 76), (card.Suggestions[0].Letter, card.Suggestions[0].Points));
        // unnecessary spending at 20%: the limit is still broken, overall 58,83
        Assert.Equal("Gereksiz harcama payın %30. 100,00 TL daha az harcasaydın %20'ye inerdi; genel notun FF yerine FD olurdu.",
            card.Suggestions[1].Text);
        Assert.Equal(("FD", 59), (card.Suggestions[1].Letter, card.Suggestions[1].Points));
    }

    [Fact]
    public async Task GetAsync_SuggestsSavingEnoughForTheNextLetterAndGivesThePointsWhenTheLetterStays()
    {
        using var t = new TestDb();
        SimpleMonth(t, September, 190m); // savings 5% -> 70 (CC), allowance 25/30: overall 81,56 = BB

        var card = await Get(t);

        Assert.Equal((82, "BB"), (card.Points, card.Letter));
        var suggestion = Assert.Single(card.Suggestions);
        // 7,5% saved is a CB: 25 more than the 50 that was left; overall 83,89
        Assert.Equal("Tasarruf notun CC. 25,00 TL daha az harcasaydın CB olurdu; genel puanın 82 yerine 84 olurdu.",
            suggestion.Text);
        Assert.Equal(("BB", 84), (suggestion.Letter, suggestion.Points));
    }

    [Fact]
    public async Task GetAsync_SkipsASavingsLetterThatWouldNotMoveTheOverallScore()
    {
        using var t = new TestDb();
        // 1.000 in, 5 x 177 out: savings 11,5% -> 83 (BB), allowance 25/30: overall 87,62, shown as 88.
        SimpleMonth(t, September, 177m);

        var card = await Get(t);

        Assert.Equal((88, "BA"), (card.Points, card.Letter));
        var suggestion = Assert.Single(card.Suggestions);
        // a BA in savings (12,5%, 10 less) gives 88,56, shown as 89: the nearest letter is enough here
        Assert.Equal("Tasarruf notun BB. 10,00 TL daha az harcasaydın BA olurdu; genel puanın 88 yerine 89 olurdu.",
            suggestion.Text);
    }

    [Fact]
    public async Task GetAsync_GoesOnToTheNextSavingsLetterWhenTheNearestOneChangesNothing()
    {
        using var t = new TestDb();
        // 1.000 in, 903 out: savings 9,7% -> 79,4 (CB), allowance 25/30: overall 85,94, shown as 86.
        // A BB in savings (3 less) gives 86,22, still shown as 86; a BA (28 less) gives 88,56.
        SimpleMonth(t, September, 180.6m);

        var card = await Get(t);

        Assert.Equal((86, "BA"), (card.Points, card.Letter));
        var suggestion = Assert.Single(card.Suggestions);
        Assert.Equal("Tasarruf notun CB. 28,00 TL daha az harcasaydın BA olurdu; genel puanın 86 yerine 89 olurdu.",
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
        Assert.Equal(["AA", null, "CC"], history.Select(h => h.Letter));
        Assert.Equal([100, null, 70], history.Select(h => h.Points));
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
