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

        Assert.Equal(16, all.Count);
    }

    [Theory]
    [InlineData(TransactionType.Income, 4)]
    [InlineData(TransactionType.Expense, 12)]
    public async Task GetByTypeAsync_ReturnsOnlyRequestedType(TransactionType type, int expectedCount)
    {
        using var db = new TestDb();
        var list = await new CategoryService(db.Context).GetByTypeAsync(type);

        Assert.Equal(expectedCount, list.Count);
        Assert.All(list, c => Assert.Equal(type, c.Type));
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
