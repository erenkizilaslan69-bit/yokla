using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class CategoryServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsSeededCategories()
    {
        using var db = new TestDb();
        var all = await new CategoryService(db.Context).GetAllAsync();

        Assert.Equal(18, all.Count);
    }

    [Fact]
    public async Task GetByTypeAsync_OffersIncomeForEveryKindOfUserWithTheCatchAllLast()
    {
        using var db = new TestDb();
        var income = await new CategoryService(db.Context).GetByTypeAsync(TransactionType.Income);

        Assert.Equal(["Maaş", "Serbest Çalışma", "Part-time İş", "KYK Bursu", "Aile Desteği", "Diğer Gelir"],
            income.Select(c => c.Name));
    }

    [Fact]
    public async Task GetAllAsync_ListsIncomeFirstInTheSameOrderThenExpensesByTheirIds()
    {
        using var db = new TestDb();
        var service = new CategoryService(db.Context);
        var all = await service.GetAllAsync();

        Assert.Equal((await service.GetByTypeAsync(TransactionType.Income)).Select(c => c.Id), all.Take(6).Select(c => c.Id));
        var expenses = all.Skip(6).ToList();
        Assert.All(expenses, c => Assert.Equal(TransactionType.Expense, c.Type));
        Assert.Equal(expenses.Select(c => c.Id).OrderBy(id => id), expenses.Select(c => c.Id));
        Assert.Equal("Market", expenses[0].Name);
    }

    [Fact]
    public async Task TheCategoriesAddedLaterDoNotMoveTheIdsOfTheOlderOnes()
    {
        using var db = new TestDb();
        var all = await new CategoryService(db.Context).GetAllAsync();

        Assert.Equal(("KYK Bursu", "Market", "Eğlence"),
            (all.Single(c => c.Id == 1).Name, all.Single(c => c.Id == 5).Name, all.Single(c => c.Id == 16).Name));
        Assert.Equal(("Maaş", "Serbest Çalışma"), (all.Single(c => c.Id == 17).Name, all.Single(c => c.Id == 18).Name));
        Assert.All(all.Where(c => c.Id > 16), c =>
        {
            Assert.Equal(TransactionType.Income, c.Type);
            Assert.False(c.IsFixed);
            Assert.False(c.IsUnnecessary);
        });
    }

    [Theory]
    [InlineData(TransactionType.Income, 6)]
    [InlineData(TransactionType.Expense, 12)]
    public async Task GetByTypeAsync_ReturnsOnlyRequestedType(TransactionType type, int expectedCount)
    {
        using var db = new TestDb();
        var list = await new CategoryService(db.Context).GetByTypeAsync(type);

        Assert.Equal(expectedCount, list.Count);
        Assert.All(list, c => Assert.Equal(type, c.Type));
    }

    [Fact]
    public async Task GetAllAsync_MarksFixedCategories()
    {
        using var db = new TestDb();
        var all = await new CategoryService(db.Context).GetAllAsync();

        var fixedNames = all.Where(c => c.IsFixed).Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(["Abonelikler", "Faturalar", "Kira/Yurt"], fixedNames);
        Assert.All(all.Where(c => c.IsFixed), c => Assert.Equal(TransactionType.Expense, c.Type));
        Assert.DoesNotContain(all, c => c.IsFixed && c.IsUnnecessary);
    }

    [Fact]
    public async Task GetAllAsync_MarksUnnecessaryCategories()
    {
        using var db = new TestDb();
        var all = await new CategoryService(db.Context).GetAllAsync();

        var unnecessary = all.Where(c => c.IsUnnecessary).Select(c => c.Name).OrderBy(n => n).ToList();
        Assert.Equal(["Dışarıda Yemek", "Eğlence", "Kafe/Kahve"], unnecessary);
    }
}
