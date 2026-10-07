using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class TransactionServiceTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const int IncomeCategory = 1;  // KYK Bursu
    private const int ExpenseCategory = 5; // Market

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
        var service = new TransactionService(db.Context);
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
        var service = new TransactionService(db.Context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Alice, Expense(amount)));
    }

    [Fact]
    public async Task CreateAsync_RejectsCategoryOfDifferentType()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        var input = Expense(10m);
        input.CategoryId = IncomeCategory;

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Alice, input));
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownCategory()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        var input = Expense(10m);
        input.CategoryId = 999;

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Alice, input));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyOwnTransactionsNewestFirst()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        await service.CreateAsync(Alice, Expense(1m, new DateTime(2026, 10, 1)));
        await service.CreateAsync(Alice, Expense(2m, new DateTime(2026, 10, 5)));
        await service.CreateAsync(Bob, Expense(3m));

        var list = await service.GetAllAsync(Alice);

        Assert.Equal(2, list.Count);
        Assert.Equal(2m, list[0].Amount);
        Assert.All(list, t => Assert.Equal(Alice, t.UserId));
        Assert.NotNull(list[0].Category);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForOtherUsersTransaction()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        var bobs = await service.CreateAsync(Bob, Expense(5m));

        Assert.Null(await service.GetAsync(Alice, bobs.Id));
        Assert.NotNull(await service.GetAsync(Bob, bobs.Id));
    }

    [Fact]
    public async Task UpdateAsync_ChangesOwnTransaction()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
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
        var service = new TransactionService(db.Context);
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
        var service = new TransactionService(db.Context);
        var created = await service.CreateAsync(Alice, Expense(10m));

        var update = Expense(0m);
        update.Id = created.Id;

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(Alice, update));
    }

    [Fact]
    public async Task DeleteAsync_RemovesOwnTransaction()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        var created = await service.CreateAsync(Alice, Expense(10m));

        Assert.True(await service.DeleteAsync(Alice, created.Id));
        Assert.Null(await service.GetAsync(Alice, created.Id));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForOtherUsersTransaction()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        var bobs = await service.CreateAsync(Bob, Expense(10m));

        Assert.False(await service.DeleteAsync(Alice, bobs.Id));
        Assert.NotNull(await service.GetAsync(Bob, bobs.Id));
    }

    [Fact]
    public async Task GetTotalsAsync_SumsPerTypeForCurrentUserOnly()
    {
        using var db = new TestDb();
        var service = new TransactionService(db.Context);
        await service.CreateAsync(Alice, Income(1000m));
        await service.CreateAsync(Alice, Expense(120.25m));
        await service.CreateAsync(Alice, Expense(79.75m));
        await service.CreateAsync(Bob, Income(5000m));

        var totals = await service.GetTotalsAsync(Alice);

        Assert.Equal(1000m, totals.Income);
        Assert.Equal(200m, totals.Expense);
        Assert.Equal(800m, totals.Balance);
    }

    [Fact]
    public async Task GetTotalsAsync_ReturnsZerosWhenEmpty()
    {
        using var db = new TestDb();

        var totals = await new TransactionService(db.Context).GetTotalsAsync(Alice);

        Assert.Equal(new TransactionTotals(0m, 0m), totals);
    }
}
