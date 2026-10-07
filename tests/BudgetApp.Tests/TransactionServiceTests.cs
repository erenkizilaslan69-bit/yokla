using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class TransactionServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const int IncomeCategory = 1;  // KYK Bursu
    private const int ExpenseCategory = 5; // Market

    /// <summary>Fixed clock so date rules do not depend on the machine's current date.</summary>
    private sealed class FixedTime(DateTime localNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(localNow, DateTimeKind.Utc));
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTime Today = new(2026, 10, 15);

    private static TransactionService NewService(TestDb db) =>
        new(db.Context, new FixedTime(Today.AddHours(13)));

    private static Transaction Expense(decimal amount, DateTime? date = null) => new()
    {
        Amount = amount,
        Type = TransactionType.Expense,
        Date = date ?? new DateTime(2026, 10, 7),
        CategoryId = ExpenseCategory
    };

    private static Transaction Income(decimal amount) => new()
    {
        Amount = amount,
        Type = TransactionType.Income,
        Date = new DateTime(2026, 10, 1),
        CategoryId = IncomeCategory
    };

    [Fact]
    public async Task CreateAsync_PersistsWithUserIdAndTrimsText()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var input = Expense(12.50m);
        input.ItemName = "  Süt 1 L ";
        input.Note = "   ";

        var created = await service.CreateAsync(Alice, input);

        Assert.True(created.Id > 0);
        Assert.Equal(Alice, created.UserId);
        Assert.Equal(12.50m, created.Amount);
        Assert.Equal("Süt 1 L", created.ItemName);
        Assert.Null(created.Note);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateAsync_RejectsNonPositiveAmount(int amount)
    {
        using var db = new TestDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Alice, Expense(amount)));
    }

    [Fact]
    public async Task CreateAsync_RejectsCategoryOfDifferentType()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var input = Expense(10m);
        input.CategoryId = IncomeCategory;

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Alice, input));
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownCategory()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var input = Expense(10m);
        input.CategoryId = 999;

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Alice, input));
    }

    [Fact]
    public async Task GetByMonthAsync_ReturnsOnlyOwnTransactionsOfThatMonthNewestFirst()
    {
        using var db = new TestDb();
        var service = NewService(db);
        await service.CreateAsync(Alice, Expense(1m, new DateTime(2026, 10, 1)));
        await service.CreateAsync(Alice, Expense(2m, new DateTime(2026, 10, 5)));
        await service.CreateAsync(Alice, Expense(9m, new DateTime(2026, 9, 30)));
        await service.CreateAsync(Bob, Expense(3m, new DateTime(2026, 10, 3)));

        var list = await service.GetByMonthAsync(Alice, 2026, 10);

        Assert.Equal(2, list.Count);
        Assert.Equal(2m, list[0].Amount);
        Assert.All(list, t => Assert.Equal(Alice, t.UserId));
        Assert.NotNull(list[0].Category);
    }

    [Fact]
    public async Task GetByMonthAsync_IncludesFirstAndLastDayOfMonth()
    {
        using var db = new TestDb();
        var service = NewService(db);
        await service.CreateAsync(Alice, Expense(1m, new DateTime(2026, 9, 1)));
        await service.CreateAsync(Alice, Expense(2m, new DateTime(2026, 9, 30)));
        await service.CreateAsync(Alice, Expense(4m, new DateTime(2026, 10, 1)));

        var list = await service.GetByMonthAsync(Alice, 2026, 9);

        Assert.Equal([2m, 1m], list.Select(t => t.Amount).ToArray());
    }

    [Fact]
    public async Task GetByMonthAsync_ReturnsEmptyForMonthWithoutTransactions()
    {
        using var db = new TestDb();
        var service = NewService(db);
        await service.CreateAsync(Alice, Expense(1m));

        Assert.Empty(await service.GetByMonthAsync(Alice, 2026, 8));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForOtherUsersTransaction()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var bobs = await service.CreateAsync(Bob, Expense(5m));

        Assert.Null(await service.GetAsync(Alice, bobs.Id));
        Assert.NotNull(await service.GetAsync(Bob, bobs.Id));
    }

    [Fact]
    public async Task UpdateAsync_ChangesOwnTransaction()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var created = await service.CreateAsync(Alice, Expense(10m));

        var update = Expense(25.75m);
        update.Id = created.Id;
        update.ItemName = "Ekmek";
        var ok = await service.UpdateAsync(Alice, update);

        Assert.True(ok);
        var reloaded = await service.GetAsync(Alice, created.Id);
        Assert.Equal(25.75m, reloaded!.Amount);
        Assert.Equal("Ekmek", reloaded.ItemName);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalseForOtherUsersTransaction()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var bobs = await service.CreateAsync(Bob, Expense(10m));

        var update = Expense(99m);
        update.Id = bobs.Id;

        Assert.False(await service.UpdateAsync(Alice, update));
        Assert.Equal(10m, (await service.GetAsync(Bob, bobs.Id))!.Amount);
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidAmount()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var created = await service.CreateAsync(Alice, Expense(10m));

        var update = Expense(0m);
        update.Id = created.Id;

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Alice, update));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOwnTransaction()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var created = await service.CreateAsync(Alice, Expense(10m));

        Assert.True(await service.DeleteAsync(Alice, created.Id));
        Assert.Null(await service.GetAsync(Alice, created.Id));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForOtherUsersTransaction()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var bobs = await service.CreateAsync(Bob, Expense(10m));

        Assert.False(await service.DeleteAsync(Alice, bobs.Id));
        Assert.NotNull(await service.GetAsync(Bob, bobs.Id));
    }

    [Fact]
    public async Task GetTotalsAsync_SumsPerTypeForCurrentUserOnly()
    {
        using var db = new TestDb();
        var service = NewService(db);
        await service.CreateAsync(Alice, Income(1000m));
        await service.CreateAsync(Alice, Expense(120.25m));
        await service.CreateAsync(Alice, Expense(79.75m));
        await service.CreateAsync(Bob, Income(5000m));

        var totals = await service.GetTotalsAsync(Alice, 2026, 10);

        Assert.Equal(1000m, totals.Income);
        Assert.Equal(200m, totals.Expense);
        Assert.Equal(800m, totals.Balance);
    }

    [Fact]
    public async Task GetTotalsAsync_CountsOnlyTheRequestedMonth()
    {
        using var db = new TestDb();
        var service = NewService(db);
        await service.CreateAsync(Alice, Income(1000m));                                     // 2026-10-01
        await service.CreateAsync(Alice, Expense(100m, new DateTime(2026, 10, 15)));
        await service.CreateAsync(Alice, Expense(777m, new DateTime(2026, 9, 30)));          // previous month
        var august = Income(5000m);
        august.Date = new DateTime(2026, 8, 20);
        await service.CreateAsync(Alice, august);                                            // older month

        var october = await service.GetTotalsAsync(Alice, 2026, 10);
        var september = await service.GetTotalsAsync(Alice, 2026, 9);

        Assert.Equal(new TransactionTotals(1000m, 100m), october);
        Assert.Equal(900m, october.Balance);
        Assert.Equal(new TransactionTotals(0m, 777m), september);
        Assert.Equal(-777m, september.Balance);
    }

    [Fact]
    public async Task GetTotalsAsync_ReturnsZerosWhenEmpty()
    {
        using var db = new TestDb();

        var totals = await NewService(db).GetTotalsAsync(Alice, 2026, 10);

        Assert.Equal(new TransactionTotals(0m, 0m), totals);
    }

    // ---------- Future dates ----------

    [Fact]
    public async Task CreateAsync_AllowsToday()
    {
        using var db = new TestDb();

        var created = await NewService(db).CreateAsync(Alice, Expense(10m, Today));

        Assert.Equal(Today, created.Date);
    }

    [Fact]
    public async Task CreateAsync_RejectsFutureDate()
    {
        using var db = new TestDb();

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService(db).CreateAsync(Alice, Expense(10m, Today.AddDays(1))));
        Assert.Empty(db.Context.Transactions);
    }

    [Fact]
    public async Task UpdateAsync_RejectsMovingTransactionToFuture()
    {
        using var db = new TestDb();
        var service = NewService(db);
        var created = await service.CreateAsync(Alice, Expense(10m, Today));

        var update = Expense(10m, Today.AddDays(3));
        update.Id = created.Id;

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Alice, update));
        Assert.Equal(Today, (await service.GetAsync(Alice, created.Id))!.Date);
    }
}

