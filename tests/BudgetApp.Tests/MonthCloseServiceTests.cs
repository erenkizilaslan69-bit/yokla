using BudgetApp.Data;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Tests;

public class MonthCloseServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const int IncomeCategory = 1;  // KYK Bursu
    private const int ExpenseCategory = 5; // Market

    // "Today" is mid-October 2026, so the month to close is September 2026.
    private static readonly DateTime Today = new(2026, 10, 15);

    private static void AddTx(ApplicationDbContext db, string userId, TransactionType type, decimal amount, DateTime date)
    {
        db.Transactions.Add(new Transaction
        {
            UserId = userId,
            Type = type,
            Amount = amount,
            Date = date,
            CategoryId = type == TransactionType.Income ? IncomeCategory : ExpenseCategory
        });
        db.SaveChanges();
    }

    private static Goal AddGoal(ApplicationDbContext db, string userId, decimal target, string name = "Kulaklık",
        bool completed = false, DateTime? createdAt = null)
    {
        var goal = new Goal
        {
            UserId = userId,
            Name = name,
            TargetAmount = target,
            CreatedAt = createdAt ?? new DateTime(2026, 8, 1),
            IsCompleted = completed
        };
        db.Goals.Add(goal);
        db.SaveChanges();
        return goal;
    }

    /// <summary>September 2026: income 2000, expense 1160 => remaining 840.</summary>
    private static void SeedSeptemberSurplus(ApplicationDbContext db, string userId = Alice)
    {
        AddTx(db, userId, TransactionType.Income, 2000m, new DateTime(2026, 9, 1));
        AddTx(db, userId, TransactionType.Expense, 1000m, new DateTime(2026, 9, 10));
        AddTx(db, userId, TransactionType.Expense, 160m, new DateTime(2026, 9, 30));
    }

    // ---------- CalculateRemainingAsync ----------

    [Fact]
    public async Task CalculateRemainingAsync_ReturnsIncomeMinusExpense()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);

        var remaining = await new MonthCloseService(t.Context).CalculateRemainingAsync(Alice, 2026, 9);

        Assert.Equal(840m, remaining);
    }

    [Fact]
    public async Task CalculateRemainingAsync_IsNegativeWhenExpensesExceedIncome()
    {
        using var t = new TestDb();
        AddTx(t.Context, Alice, TransactionType.Income, 500m, new DateTime(2026, 9, 1));
        AddTx(t.Context, Alice, TransactionType.Expense, 800m, new DateTime(2026, 9, 2));

        var remaining = await new MonthCloseService(t.Context).CalculateRemainingAsync(Alice, 2026, 9);

        Assert.Equal(-300m, remaining);
    }

    [Fact]
    public async Task CalculateRemainingAsync_CountsOnlyTheRequestedMonth()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddTx(t.Context, Alice, TransactionType.Income, 9999m, new DateTime(2026, 8, 31));
        AddTx(t.Context, Alice, TransactionType.Expense, 7777m, new DateTime(2026, 10, 1));

        var remaining = await new MonthCloseService(t.Context).CalculateRemainingAsync(Alice, 2026, 9);

        Assert.Equal(840m, remaining);
    }

    [Fact]
    public async Task CalculateRemainingAsync_CountsOnlyTheGivenUser()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddTx(t.Context, Bob, TransactionType.Income, 5000m, new DateTime(2026, 9, 5));

        var remaining = await new MonthCloseService(t.Context).CalculateRemainingAsync(Alice, 2026, 9);

        Assert.Equal(840m, remaining);
    }

    [Fact]
    public async Task CalculateRemainingAsync_ReturnsZeroWhenNoTransactions()
    {
        using var t = new TestDb();

        var remaining = await new MonthCloseService(t.Context).CalculateRemainingAsync(Alice, 2026, 9);

        Assert.Equal(0m, remaining);
    }

    // ---------- GetPendingAsync ----------

    [Fact]
    public async Task GetPendingAsync_ReturnsCardForPreviousMonthWhenNotClosed()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
        Assert.Equal(2026, pending.Year);
        Assert.Equal(9, pending.Month);
        Assert.Equal(840m, pending.Remaining);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsNullWhenPreviousMonthAlreadyClosed()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        t.Context.MonthCloses.Add(new MonthClose { UserId = Alice, Year = 2026, Month = 9, Remaining = 840m });
        t.Context.SaveChanges();

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Null(pending);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsNullWhenPreviousMonthHasNoTransactions()
    {
        using var t = new TestDb();
        // Only an old transaction exists; September is empty.
        AddTx(t.Context, Alice, TransactionType.Income, 1000m, new DateTime(2026, 7, 1));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Null(pending);
    }

    [Fact]
    public async Task GetPendingAsync_OffersActiveGoalWhenRemainingIsPositive()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending?.TargetGoal);
        Assert.Equal(goal.Id, pending.TargetGoal.Id);
    }

    [Fact]
    public async Task GetPendingAsync_HasNoTargetGoalWhenUserHasNoGoal()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task GetPendingAsync_HasNoTargetGoalWhenRemainingIsNotPositive()
    {
        using var t = new TestDb();
        AddTx(t.Context, Alice, TransactionType.Income, 500m, new DateTime(2026, 9, 1));
        AddTx(t.Context, Alice, TransactionType.Expense, 800m, new DateTime(2026, 9, 2));
        AddGoal(t.Context, Alice, 3000m);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
        Assert.Equal(-300m, pending.Remaining);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task GetPendingAsync_IgnoresCompletedGoals()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Alice, 3000m, completed: true);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task GetPendingAsync_PicksOldestIncompleteGoal()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Alice, 5000m, "Laptop", createdAt: new DateTime(2026, 9, 1));
        var oldest = AddGoal(t.Context, Alice, 3000m, "Kulaklık", createdAt: new DateTime(2026, 7, 1));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(oldest.Id, pending?.TargetGoal?.Id);
    }

    [Fact]
    public async Task GetPendingAsync_DoesNotOfferAnotherUsersGoal()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Bob, 3000m);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task GetPendingAsync_InJanuaryLooksAtDecemberOfPreviousYear()
    {
        using var t = new TestDb();
        AddTx(t.Context, Alice, TransactionType.Income, 1500m, new DateTime(2025, 12, 5));
        AddTx(t.Context, Alice, TransactionType.Expense, 400m, new DateTime(2025, 12, 20));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, new DateTime(2026, 1, 3));

        Assert.NotNull(pending);
        Assert.Equal(2025, pending.Year);
        Assert.Equal(12, pending.Month);
        Assert.Equal(1100m, pending.Remaining);
    }

    [Fact]
    public async Task GetPendingAsync_OtherUsersCloseDoesNotHideCard()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        t.Context.MonthCloses.Add(new MonthClose { UserId = Bob, Year = 2026, Month = 9, Remaining = 1m });
        t.Context.SaveChanges();

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
    }

    // ---------- CloseAsync ----------

    [Fact]
    public async Task CloseAsync_TransferCreatesContributionAndLinksGoal()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Equal(goal.Id, close.TransferredGoalId);
        var contribution = await t.Context.GoalContributions.SingleAsync();
        Assert.Equal(goal.Id, contribution.GoalId);
        Assert.Equal(840m, contribution.Amount);
        Assert.Equal(GoalContributionSource.MonthClose, contribution.Source);
    }

    [Fact]
    public async Task CloseAsync_DeclinedTransferOnlySavesMonthClose()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Alice, 3000m);

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: false, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.Empty(t.Context.GoalContributions);
        Assert.Single(t.Context.MonthCloses);
    }

    [Fact]
    public async Task CloseAsync_NegativeRemainingNeverTransfers()
    {
        using var t = new TestDb();
        AddTx(t.Context, Alice, TransactionType.Income, 500m, new DateTime(2026, 9, 1));
        AddTx(t.Context, Alice, TransactionType.Expense, 800m, new DateTime(2026, 9, 2));
        AddGoal(t.Context, Alice, 3000m);

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(-300m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.Empty(t.Context.GoalContributions);
    }

    [Fact]
    public async Task CloseAsync_ZeroRemainingNeverTransfers()
    {
        using var t = new TestDb();
        AddTx(t.Context, Alice, TransactionType.Income, 500m, new DateTime(2026, 9, 1));
        AddTx(t.Context, Alice, TransactionType.Expense, 500m, new DateTime(2026, 9, 2));
        AddGoal(t.Context, Alice, 3000m);

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(0m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.Empty(t.Context.GoalContributions);
    }

    [Fact]
    public async Task CloseAsync_TransferWithoutGoalStillSavesMonthClose()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.Empty(t.Context.GoalContributions);
        Assert.Single(t.Context.MonthCloses);
    }

    [Fact]
    public async Task CloseAsync_ThrowsWhenMonthAlreadyClosed()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var service = new MonthCloseService(t.Context);
        await service.CloseAsync(Alice, 2026, 9, transfer: false, Today);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CloseAsync(Alice, 2026, 9, transfer: true, Today));
        Assert.Single(t.Context.MonthCloses);
    }

    [Fact]
    public async Task CloseAsync_DoesNotDoubleTransferOnSecondAttempt()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Alice, 3000m);
        var service = new MonthCloseService(t.Context);
        await service.CloseAsync(Alice, 2026, 9, transfer: true, Today);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CloseAsync(Alice, 2026, 9, transfer: true, Today));
        Assert.Single(t.Context.GoalContributions);
    }

    [Theory]
    [InlineData(2026, 10)] // current month
    [InlineData(2026, 11)] // future month
    [InlineData(2027, 1)]  // future year
    public async Task CloseAsync_ThrowsForCurrentOrFutureMonth(int year, int month)
    {
        using var t = new TestDb();
        var service = new MonthCloseService(t.Context);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CloseAsync(Alice, year, month, transfer: false, Today));
        Assert.Empty(t.Context.MonthCloses);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public async Task CloseAsync_ThrowsForInvalidMonthNumber(int year, int month)
    {
        using var t = new TestDb();
        var service = new MonthCloseService(t.Context);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CloseAsync(Alice, year, month, transfer: false, Today));
    }

    [Fact]
    public async Task CloseAsync_MarksGoalCompletedWhenContributionsReachTarget()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 800m); // 840 >= 800

        await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        var reloaded = await t.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id);
        Assert.True(reloaded.IsCompleted);
    }

    [Fact]
    public async Task CloseAsync_LeavesGoalIncompleteWhenTargetNotReached()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);

        await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        var reloaded = await t.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id);
        Assert.False(reloaded.IsCompleted);
    }

    [Fact]
    public async Task CloseAsync_CompletionCountsEarlierContributions()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 1000m);
        // Dated August, i.e. before the month being closed: money added in September itself would now be deducted
        // from the transfer (see the "AlreadyAdded" tests below), this test is about earlier contributions.
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 200m, Date = new DateTime(2026, 8, 5), Source = GoalContributionSource.Manual
        });
        t.Context.SaveChanges();

        await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today); // 200 + 840 >= 1000

        var reloaded = await t.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id);
        Assert.True(reloaded.IsCompleted);
    }

    [Fact]
    public async Task CloseAsync_CapsTransferAtGoalsMissingAmountButStoresFullRemaining()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context); // remaining 840
        var goal = AddGoal(t.Context, Alice, 1000m);
        // Dated August (before the closed month), see the note on CloseAsync_CompletionCountsEarlierContributions.
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 700m, Date = new DateTime(2026, 8, 5), Source = GoalContributionSource.Manual
        });
        t.Context.SaveChanges(); // 300 TL missing

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Equal(goal.Id, close.TransferredGoalId);
        var transfer = await t.Context.GoalContributions.SingleAsync(c => c.Source == GoalContributionSource.MonthClose);
        Assert.Equal(300m, transfer.Amount);
        var reloaded = await t.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id);
        Assert.True(reloaded.IsCompleted);
    }

    // ---------- money the user already added by hand during the month ----------

    private static DateTime Sep(int day) => new(2026, 9, day);

    private static void AddContribution(TestDb t, int goalId, decimal amount, DateTime date,
        GoalContributionSource source = GoalContributionSource.Manual)
    {
        t.Context.GoalContributions.Add(new GoalContribution { GoalId = goalId, Amount = amount, Date = date, Source = source });
        t.Context.SaveChanges();
    }

    [Fact]
    public async Task GetPendingAsync_SubtractsManualContributionsOfThatMonthFromTheTransferableAmount()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context); // remaining 840
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 200m, Sep(10));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);
        Assert.Equal(840m, pending.Remaining);       // the real balance is untouched
        Assert.Equal(200m, pending.AlreadyAdded);
        Assert.Equal(640m, pending.Transferable);
        Assert.Equal(goal.Id, pending.TargetGoal?.Id);
    }

    [Fact]
    public async Task GetPendingAsync_TransferableEqualsRemainingWhenNothingWasAddedByHand()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Alice, 3000m);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(0m, pending!.AlreadyAdded);
        Assert.Equal(840m, pending.Transferable);
    }

    [Fact]
    public async Task GetPendingAsync_OnlyCountsManualContributionsMadeInThatMonth()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 100m, new DateTime(2026, 8, 31));  // the day before September
        AddContribution(t, goal.Id, 100m, new DateTime(2026, 10, 1));  // the day after September
        AddContribution(t, goal.Id, 30m, Sep(1));                      // first day counts
        AddContribution(t, goal.Id, 20m, Sep(30));                     // last day counts

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(50m, pending!.AlreadyAdded);
        Assert.Equal(790m, pending.Transferable);
    }

    [Fact]
    public async Task GetPendingAsync_IgnoresContributionsThatCameFromAMonthClose()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 500m, Sep(10), GoalContributionSource.MonthClose);

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(0m, pending!.AlreadyAdded);
        Assert.Equal(840m, pending.Transferable);
    }

    [Fact]
    public async Task GetPendingAsync_CountsManualContributionsToAllOfTheUsersGoals()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var first = AddGoal(t.Context, Alice, 3000m, "Kulaklık", createdAt: new DateTime(2026, 7, 1));
        var second = AddGoal(t.Context, Alice, 5000m, "Laptop", createdAt: new DateTime(2026, 8, 1));
        AddContribution(t, first.Id, 100m, Sep(5));
        AddContribution(t, second.Id, 150m, Sep(6));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(250m, pending!.AlreadyAdded);
        Assert.Equal(590m, pending.Transferable);
        Assert.Equal(first.Id, pending.TargetGoal?.Id); // the transfer still goes to the oldest open goal
    }

    [Fact]
    public async Task GetPendingAsync_IgnoresOtherUsersManualContributions()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Alice, 3000m);
        var bobsGoal = AddGoal(t.Context, Bob, 3000m, "Bobun");
        AddContribution(t, bobsGoal.Id, 500m, Sep(10));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(0m, pending!.AlreadyAdded);
        Assert.Equal(840m, pending.Transferable);
    }

    [Fact]
    public async Task GetPendingAsync_OffersNoTransferWhenManualContributionsCoverTheWholeSurplus()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 840m, Sep(10));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.NotNull(pending);                      // the card is still shown, as an information message
        Assert.Equal(840m, pending.Remaining);
        Assert.Equal(840m, pending.AlreadyAdded);
        Assert.Equal(0m, pending.Transferable);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task GetPendingAsync_TransferableIsNeverNegativeWhenMoreWasAddedThanTheSurplus()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 5000m);
        AddContribution(t, goal.Id, 1000m, Sep(10));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(1000m, pending!.AlreadyAdded);
        Assert.Equal(0m, pending.Transferable);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task GetPendingAsync_TransferableIsZeroForADeficitEvenWithManualContributions()
    {
        using var t = new TestDb();
        AddTx(t.Context, Alice, TransactionType.Income, 500m, Sep(1));
        AddTx(t.Context, Alice, TransactionType.Expense, 800m, Sep(2));
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 100m, Sep(10));

        var pending = await new MonthCloseService(t.Context).GetPendingAsync(Alice, Today);

        Assert.Equal(-300m, pending!.Remaining);
        Assert.Equal(0m, pending.Transferable);
        Assert.Null(pending.TargetGoal);
    }

    [Fact]
    public async Task CloseAsync_TransfersOnlyTheSurplusThatWasNotAlreadyAddedByHand()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context); // remaining 840
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 200m, Sep(10));

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);          // MonthClose keeps the month's real balance
        Assert.Equal(goal.Id, close.TransferredGoalId);
        var transfer = await t.Context.GoalContributions.SingleAsync(c => c.Source == GoalContributionSource.MonthClose);
        Assert.Equal(640m, transfer.Amount);
        // The 840 TL surplus ends up in the goal exactly once: 200 by hand + 640 by the transfer.
        Assert.Equal(840m, t.Context.GoalContributions.AsEnumerable().Sum(c => c.Amount));
    }

    [Fact]
    public async Task CloseAsync_DoesNotTransferWhenManualContributionsCoverTheWholeSurplus()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 840m, Sep(10));

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.DoesNotContain(t.Context.GoalContributions, c => c.Source == GoalContributionSource.MonthClose);
        Assert.Single(t.Context.MonthCloses);          // the month is still closed
    }

    [Fact]
    public async Task CloseAsync_DoesNotTransferWhenMoreWasAddedByHandThanTheSurplus()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 5000m);
        AddContribution(t, goal.Id, 1000m, Sep(10));

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.Single(t.Context.GoalContributions);    // only the manual one
    }

    [Fact]
    public async Task CloseAsync_StillStoresTheRealRemainingWhenTheTransferIsDeclined()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);
        AddContribution(t, goal.Id, 200m, Sep(10));

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: false, Today);

        Assert.Equal(840m, close.Remaining);
        Assert.Null(close.TransferredGoalId);
        Assert.Single(t.Context.GoalContributions);
    }

    [Fact]
    public async Task CloseAsync_StillCapsTheReducedTransferAtTheGoalsMissingAmount()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context); // remaining 840
        var goal = AddGoal(t.Context, Alice, 800m);
        AddContribution(t, goal.Id, 100m, Sep(10)); // transferable 740, but the goal only needs 700 more

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(840m, close.Remaining);
        var transfer = await t.Context.GoalContributions.SingleAsync(c => c.Source == GoalContributionSource.MonthClose);
        Assert.Equal(700m, transfer.Amount);
        Assert.True((await t.Context.Goals.AsNoTracking().SingleAsync(g => g.Id == goal.Id)).IsCompleted);
    }

    [Fact]
    public async Task CloseAsync_ManualContributionsToAnotherGoalAlsoReduceTheTransfer()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var oldest = AddGoal(t.Context, Alice, 3000m, "Kulaklık", createdAt: new DateTime(2026, 7, 1));
        var newer = AddGoal(t.Context, Alice, 5000m, "Laptop", createdAt: new DateTime(2026, 8, 1));
        AddContribution(t, newer.Id, 300m, Sep(10));

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Equal(oldest.Id, close.TransferredGoalId);
        var transfer = await t.Context.GoalContributions.SingleAsync(c => c.Source == GoalContributionSource.MonthClose);
        Assert.Equal(oldest.Id, transfer.GoalId);
        Assert.Equal(540m, transfer.Amount);
    }

    [Fact]
    public async Task CloseAsync_IgnoresManualContributionsFromOtherMonthsAndOtherUsers()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        var goal = AddGoal(t.Context, Alice, 3000m);
        var bobsGoal = AddGoal(t.Context, Bob, 3000m, "Bobun");
        AddContribution(t, goal.Id, 200m, new DateTime(2026, 8, 20));
        AddContribution(t, bobsGoal.Id, 500m, Sep(10));

        await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        var transfer = await t.Context.GoalContributions.SingleAsync(c => c.Source == GoalContributionSource.MonthClose);
        Assert.Equal(840m, transfer.Amount);
    }

    [Fact]
    public async Task CloseAsync_AfterAManualAdd_TheSameMoneyIsNotCountedTwice()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context); // remaining 840
        var goals = new GoalService(t.Context);
        var goal = await goals.CreateAsync(Alice, "Kulaklık", 3000m, new DateTime(2026, 8, 1));
        await goals.AddManualContributionAsync(Alice, goal.Id, 500m, Sep(20));

        await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        var progress = await goals.GetActiveAsync(Alice);
        Assert.Equal(840m, progress!.Saved); // 500 by hand + 340 transferred, not 1.340
    }

    [Fact]
    public async Task CloseAsync_TransfersFullRemainingWhenBelowMissingAmount()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context); // remaining 840
        AddGoal(t.Context, Alice, 3000m);

        await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        var transfer = await t.Context.GoalContributions.SingleAsync();
        Assert.Equal(840m, transfer.Amount);
    }

    [Fact]
    public async Task CloseAsync_ComputesRemainingOnServerAndOnlyForThatUser()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddTx(t.Context, Bob, TransactionType.Income, 5000m, new DateTime(2026, 9, 5));

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: false, Today);

        Assert.Equal(Alice, close.UserId);
        Assert.Equal(840m, close.Remaining);
    }

    [Fact]
    public async Task CloseAsync_ClosingIsPerUser()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context, Alice);
        SeedSeptemberSurplus(t.Context, Bob);
        var service = new MonthCloseService(t.Context);

        await service.CloseAsync(Alice, 2026, 9, transfer: false, Today);
        var bobsClose = await service.CloseAsync(Bob, 2026, 9, transfer: false, Today);

        Assert.Equal(Bob, bobsClose.UserId);
        Assert.Equal(2, t.Context.MonthCloses.Count());
    }

    [Fact]
    public async Task CloseAsync_TransferGoesOnlyToOwnGoal()
    {
        using var t = new TestDb();
        SeedSeptemberSurplus(t.Context);
        AddGoal(t.Context, Bob, 3000m);

        var close = await new MonthCloseService(t.Context).CloseAsync(Alice, 2026, 9, transfer: true, Today);

        Assert.Null(close.TransferredGoalId);
        Assert.Empty(t.Context.GoalContributions);
    }
}
