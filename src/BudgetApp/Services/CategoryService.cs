using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public interface ICategoryService
{
    /// <summary>Income categories first, then expenses, each in the order they are offered in forms.</summary>
    Task<List<Category>> GetAllAsync();

    Task<List<Category>> GetByTypeAsync(TransactionType type);
}

public class CategoryService(ApplicationDbContext db) : ICategoryService
{
    /// <summary>
    /// The order income categories are offered in: the most common kinds of income first, the catch-all last.
    /// Ids cannot say this, because categories added later get the highest ids. Categories not named here keep
    /// their id order and come after these.
    /// </summary>
    private static readonly string[] IncomeOrder =
        ["Maaş", "Serbest Çalışma", "Part-time İş", "KYK Bursu", "Aile Desteği", "Diğer Gelir"];

    public async Task<List<Category>> GetAllAsync() =>
        Sort(await db.Categories.AsNoTracking().ToListAsync());

    public async Task<List<Category>> GetByTypeAsync(TransactionType type) =>
        Sort(await db.Categories.AsNoTracking().Where(c => c.Type == type).ToListAsync());

    private static List<Category> Sort(List<Category> categories) =>
        categories
            .OrderBy(c => c.Type)
            .ThenBy(c => Array.IndexOf(IncomeOrder, c.Name) is var index and >= 0 ? index : int.MaxValue)
            .ThenBy(c => c.Id)
            .ToList();
}
