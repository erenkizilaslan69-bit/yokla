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
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 200m, Date = new DateTime(2026, 9, 5), Source = GoalContributionSource.Manual
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
        t.Context.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 700m, Date = new DateTime(2026, 9, 5), Source = GoalContributionSource.Manual
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
