using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Tests;

public class GoalServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly DateTime Now = new(2026, 10, 15);

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_PersistsGoalForUser()
    {
        using var t = new TestDb();

        var goal = await new GoalService(t.Context).CreateAsync(Alice, "  Kulaklık ", 3000m, Now);

        Assert.True(goal.Id > 0);
        Assert.Equal(Alice, goal.UserId);
        Assert.Equal("Kulaklık", goal.Name);
        Assert.Equal(3000m, goal.TargetAmount);
        Assert.Equal(Now, goal.CreatedAt);
        Assert.False(goal.IsCompleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsEmptyName(string name)
    {
        using var t = new TestDb();

        await Assert.ThrowsAsync<ArgumentException>(() => new GoalService(t.Context).CreateAsync(Alice, name, 100m, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_RejectsNonPositiveTarget(int target)
    {
        using var t = new TestDb();

        await Assert.ThrowsAsync<ArgumentException>(() => new GoalService(t.Context).CreateAsync(Alice, "Laptop", target, Now));
    }

    // ---------- AddContributionAsync ----------

    [Fact]
    public async Task AddContributionAsync_SavesContributionAndUpdatesProgress()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 3000m, Now);

        var contribution = await service.AddContributionAsync(Alice, goal.Id, 500m, GoalContributionSource.Manual, Now);

        Assert.NotNull(contribution);
        Assert.Equal(500m, contribution.Amount);
        Assert.Equal(GoalContributionSource.Manual, contribution.Source);
        var progress = await service.GetActiveAsync(Alice);
        Assert.Equal(500m, progress!.Saved);
    }

    [Fact]
    public async Task AddContributionAsync_CompletesGoalWhenTargetReached()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);

        await service.AddContributionAsync(Alice, goal.Id, 400m, GoalContributionSource.Manual, Now);
        Assert.False((await t.Context.Goals.AsNoTracking().SingleAsync()).IsCompleted);

        await service.AddContributionAsync(Alice, goal.Id, 600m, GoalContributionSource.Manual, Now);
        Assert.True((await t.Context.Goals.AsNoTracking().SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task AddContributionAsync_ReturnsNullForOtherUsersGoal()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var bobs = await service.CreateAsync(Bob, "Laptop", 5000m, Now);

        var result = await service.AddContributionAsync(Alice, bobs.Id, 100m, GoalContributionSource.Manual, Now);

        Assert.Null(result);
        Assert.Empty(t.Context.GoalContributions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task AddContributionAsync_RejectsNonPositiveAmount(int amount)
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddContributionAsync(Alice, goal.Id, amount, GoalContributionSource.Manual, Now));
    }

    [Fact]
    public async Task AddContributionAsync_RejectsCompletedGoal()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 100m, Now);
        await service.AddContributionAsync(Alice, goal.Id, 100m, GoalContributionSource.Manual, Now);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddContributionAsync(Alice, goal.Id, 10m, GoalContributionSource.Manual, Now));
    }

    // ---------- AddManualContributionAsync ----------

    [Fact]
    public async Task AddManualContributionAsync_AddsTheFullAmountWhenItFits()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);

        var result = await service.AddManualContributionAsync(Alice, goal.Id, 250m, Now);

        Assert.NotNull(result);
        Assert.Equal(250m, result.Requested);
        Assert.Equal(250m, result.Added);
        Assert.False(result.WasCapped);
        Assert.False(result.Completed);
        var contribution = await t.Context.GoalContributions.SingleAsync();
        Assert.Equal(250m, contribution.Amount);
        Assert.Equal(GoalContributionSource.Manual, contribution.Source);
        Assert.Equal(Now.Date, contribution.Date);
    }

    [Fact]
    public async Task AddManualContributionAsync_ExactlyTheMissingAmountCompletesTheGoalWithoutCapping()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);
        await service.AddManualContributionAsync(Alice, goal.Id, 700m, Now);

        var result = await service.AddManualContributionAsync(Alice, goal.Id, 300m, Now);

        Assert.Equal(300m, result!.Added);
        Assert.False(result.WasCapped);
        Assert.True(result.Completed);
        Assert.True((await t.Context.Goals.AsNoTracking().SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task AddManualContributionAsync_CapsTheAmountAtWhatTheGoalStillNeeds()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);
        await service.AddManualContributionAsync(Alice, goal.Id, 700m, Now); // 300 TL missing

        var result = await service.AddManualContributionAsync(Alice, goal.Id, 500m, Now);

        Assert.Equal(500m, result!.Requested);
        Assert.Equal(300m, result.Added);
        Assert.True(result.WasCapped);
        Assert.True(result.Completed);
        Assert.Equal("Kulaklık", result.Goal.Name);
        Assert.Equal(1000m, t.Context.GoalContributions.AsEnumerable().Sum(c => c.Amount)); // exactly the target
        Assert.True((await t.Context.Goals.AsNoTracking().SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task AddManualContributionAsync_CountsContributionsFromEverySourceWhenWorkingOutTheMissingAmount()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);
        await service.AddContributionAsync(Alice, goal.Id, 600m, GoalContributionSource.MonthClose, Now);
        await service.AddContributionAsync(Alice, goal.Id, 100m, GoalContributionSource.Manual, Now); // 300 missing

        var result = await service.AddManualContributionAsync(Alice, goal.Id, 400m, Now);

        Assert.Equal(300m, result!.Added);
        Assert.True(result.WasCapped);
    }

    [Fact]
    public async Task AddManualContributionAsync_RoundsTheAmountToTwoDecimals()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);

        var result = await service.AddManualContributionAsync(Alice, goal.Id, 10.005m, Now);

        Assert.Equal(10.01m, result!.Requested);
        Assert.Equal(10.01m, result.Added);
        Assert.Equal(10.01m, (await t.Context.GoalContributions.SingleAsync()).Amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0.004")] // rounds to zero
    public async Task AddManualContributionAsync_RejectsAmountsThatAreNotPositive(string text)
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 1000m, Now);
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddManualContributionAsync(Alice, goal.Id, amount, Now));
        Assert.Empty(t.Context.GoalContributions);
    }

    [Fact]
    public async Task AddManualContributionAsync_RejectsACompletedGoal()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var goal = await service.CreateAsync(Alice, "Kulaklık", 100m, Now);
        await service.AddManualContributionAsync(Alice, goal.Id, 100m, Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddManualContributionAsync(Alice, goal.Id, 10m, Now));
        Assert.Single(t.Context.GoalContributions);
    }

    [Fact]
    public async Task AddManualContributionAsync_ReturnsNullForAnotherUsersGoalAndChangesNothing()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var bobs = await service.CreateAsync(Bob, "Laptop", 5000m, Now);

        var result = await service.AddManualContributionAsync(Alice, bobs.Id, 100m, Now);

        Assert.Null(result);
        Assert.Empty(t.Context.GoalContributions);
    }

    [Fact]
    public async Task AddManualContributionAsync_ReturnsNullForAnUnknownGoal()
    {
        using var t = new TestDb();

        Assert.Null(await new GoalService(t.Context).AddManualContributionAsync(Alice, 999, 10m, Now));
    }

    // ---------- GetActiveAsync ----------

    [Fact]
    public async Task GetActiveAsync_ReturnsNullWhenUserHasNoGoal()
    {
        using var t = new TestDb();

        Assert.Null(await new GoalService(t.Context).GetActiveAsync(Alice));
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsOldestIncompleteGoalOfUser()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var done = await service.CreateAsync(Alice, "Eski", 100m, new DateTime(2026, 5, 1));
        await service.AddContributionAsync(Alice, done.Id, 100m, GoalContributionSource.Manual, Now);
        var active = await service.CreateAsync(Alice, "Kulaklık", 3000m, new DateTime(2026, 6, 1));
        await service.CreateAsync(Alice, "Laptop", 20000m, new DateTime(2026, 7, 1));
        await service.CreateAsync(Bob, "Bobun", 1m, new DateTime(2026, 1, 1));

        var progress = await service.GetActiveAsync(Alice);

        Assert.Equal(active.Id, progress!.Goal.Id);
    }

    // ---------- GetAllAsync / GoalProgress ----------

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyOwnGoalsWithActiveFirst()
    {
        using var t = new TestDb();
        var service = new GoalService(t.Context);
        var done = await service.CreateAsync(Alice, "Bitti", 100m, new DateTime(2026, 1, 1));
        await service.AddContributionAsync(Alice, done.Id, 100m, GoalContributionSource.Manual, Now);
        await service.CreateAsync(Alice, "Devam", 500m, new DateTime(2026, 2, 1));
        await service.CreateAsync(Bob, "Bobun", 1m, Now);

        var all = await service.GetAllAsync(Alice);

        Assert.Equal(["Devam", "Bitti"], all.Select(p => p.Goal.Name).ToArray());
    }

    [Fact]
    public void GoalProgress_ComputesPercentAndMissing()
    {
        var goal = new Goal { TargetAmount = 3000m };

        var progress = new GoalProgress(goal, 1200m);

        Assert.Equal(40, progress.Percent);
        Assert.Equal(1800m, progress.Missing);
    }

    [Fact]
    public void GoalProgress_ClampsWhenOverTarget()
    {
        var progress = new GoalProgress(new Goal { TargetAmount = 100m }, 250m);

        Assert.Equal(100, progress.Percent);
        Assert.Equal(0m, progress.Missing);
    }

    [Fact]
    public void GoalProgress_IsZeroPercentWithoutSavings()
    {
        var progress = new GoalProgress(new Goal { TargetAmount = 100m }, 0m);

        Assert.Equal(0, progress.Percent);
        Assert.Equal(100m, progress.Missing);
    }
}
