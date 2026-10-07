using BudgetApp.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Transaction>(e =>
        {
            e.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            e.Property(t => t.ItemName).HasMaxLength(200);
            e.Property(t => t.Note).HasMaxLength(500);
            e.Property(t => t.UserId).IsRequired();
            e.HasIndex(t => new { t.UserId, t.Date });
            e.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Category>().HasData(DefaultCategories());
    }

    private static Category[] DefaultCategories()
    {
        var id = 0;
        Category C(string name, TransactionType type, bool unnecessary = false) =>
            new() { Id = ++id, Name = name, Type = type, IsUnnecessary = unnecessary };

        return
        [
            C("KYK Bursu", TransactionType.Income),
            C("Aile Desteği", TransactionType.Income),
            C("Part-time İş", TransactionType.Income),
            C("Diğer Gelir", TransactionType.Income),
            C("Market", TransactionType.Expense),
            C("Kira/Yurt", TransactionType.Expense),
            C("Faturalar", TransactionType.Expense),
            C("Ulaşım", TransactionType.Expense),
            C("Eğitim/Kırtasiye", TransactionType.Expense),
            C("Sağlık", TransactionType.Expense),
            C("Giyim", TransactionType.Expense),
            C("Abonelikler", TransactionType.Expense),
            C("Diğer Gider", TransactionType.Expense),
            C("Dışarıda Yemek", TransactionType.Expense, true),
            C("Kafe/Kahve", TransactionType.Expense, true),
            C("Eğlence", TransactionType.Expense, true)
        ];
    }
}
