using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

public interface ICategoryService
{
    Task<List<Category>> GetAllAsync();
    Task<List<Category>> GetByTypeAsync(TransactionType type);
}

public class CategoryService(ApplicationDbContext db) : ICategoryService
{
    public Task<List<Category>> GetAllAsync() =>
        db.Categories.AsNoTracking().OrderBy(c => c.Type).ThenBy(c => c.Id).ToListAsync();

    public Task<List<Category>> GetByTypeAsync(TransactionType type) =>
        db.Categories.AsNoTracking().Where(c => c.Type == type).OrderBy(c => c.Id).ToListAsync();
}
