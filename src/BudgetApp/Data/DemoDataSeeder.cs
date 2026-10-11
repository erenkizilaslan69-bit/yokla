using BudgetApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Data;

/// <summary>
/// Development-only demo account with three months of realistic student finances, so the analysis page,
/// warnings and forecasts have something meaningful to show. Dates are relative to "today".
/// </summary>
public static class DemoDataSeeder
{
    public const string Email = "demo@budgetapp.local";
    public const string Password = "Demo123!";

    private const string GoalName = "Kulaklık";
    private const decimal GoalTarget = 4500m;

    /// <summary>One planned transaction; <see cref="Day"/> is the day of the month it happens on.</summary>
    private record Entry(int Day, string Category, TransactionType Type, decimal Amount, string? Item = null);

    /// <summary>What differs between months; everything else (rent, scholarship, weekly shopping) is identical.</summary>
    private record MonthPlan(
        decimal PartTime, int PartTimeDay, decimal Electricity,
        decimal CoffeePrice, int[] CoffeeDays,
        Entry[] Extras);

    // Oldest completed month first. Coffee and eating out creep up month after month on purpose.
    private static readonly MonthPlan OldestMonth = new(
        PartTime: 1800m, PartTimeDay: 20, Electricity: 260m,
        CoffeePrice: 80m, CoffeeDays: [3, 8, 12, 17, 22, 27],
        Extras:
        [
            Out(6, "Döner", 140m), Out(15, "Pizza", 150m), Out(21, "Öğle yemeği", 130m), Out(26, "Döner", 140m),
            Fun(9, "Sinema bileti", 150m),
            Edu(4, "Ders kitabı", 200m)
        ]);

    private static readonly MonthPlan MiddleMonth = new(
        PartTime: 1200m, PartTimeDay: 22, Electricity: 310m,
        CoffeePrice: 85m, CoffeeDays: [3, 7, 11, 15, 19, 23, 27],
        Extras:
        [
            Out(8, "Döner", 145m), Out(16, "Pizza", 145m), Out(25, "Öğle yemeği", 145m),
            Fun(13, "Sinema bileti", 150m),
            Edu(14, "Fotokopi", 60m)
        ]);

    // Left open on purpose: the home page offers to move its surplus to the goal.
    private static readonly MonthPlan LastMonth = new(
        PartTime: 2000m, PartTimeDay: 21, Electricity: 290m,
        CoffeePrice: 90m, CoffeeDays: [2, 5, 8, 11, 14, 17, 20, 22, 24, 26, 28],
        Extras:
        [
            Out(4, "Döner", 150m), Out(11, "Pizza", 150m), Out(18, "Öğle yemeği", 150m), Out(27, "Döner", 150m),
            Fun(7, "Sinema bileti", 150m), Fun(19, "Konser bileti", 300m),
            Edu(3, "Kırtasiye", 120m)
        ]);

    private static readonly MonthPlan CurrentMonth = new(
        PartTime: 1500m, PartTimeDay: 20, Electricity: 300m,
        CoffeePrice: 95m, CoffeeDays: [3, 6, 9, 12, 15, 18, 21, 24, 27],
        Extras:
        [
            Out(7, "Döner", 160m), Out(13, "Pizza", 220m), Out(19, "Öğle yemeği", 130m), Out(25, "Döner", 160m),
            Fun(6, "Sinema bileti", 150m), Fun(11, "Steam oyunu", 250m),
            Edu(9, "Fotokopi", 45m)
        ]);

    private static Entry Out(int day, string item, decimal amount) =>
        new(day, "Dışarıda Yemek", TransactionType.Expense, amount, item);

    private static Entry Fun(int day, string item, decimal amount) =>
        new(day, "Eğlence", TransactionType.Expense, amount, item);

    private static Entry Edu(int day, string item, decimal amount) =>
        new(day, "Eğitim/Kırtasiye", TransactionType.Expense, amount, item);

    /// <summary>Creates the demo user (if missing) and seeds its data (if it has none).</summary>
    public static async Task SeedAsync(IServiceProvider services, DateTime today)
    {
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();

        var user = await userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            user = new IdentityUser { UserName = Email, Email = Email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, Password);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    "Could not create the demo user: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        await SeedDataAsync(db, user.Id, today);
    }

    /// <summary>
    /// Seeds the last three completed months plus the current month (up to <paramref name="today"/>),
    /// a goal and the month-close records of the two oldest months. Returns false if the user already has data.
    /// </summary>
    public static async Task<bool> SeedDataAsync(ApplicationDbContext db, string userId, DateTime today)
    {
        if (await db.Transactions.AnyAsync(t => t.UserId == userId) || await db.Goals.AnyAsync(g => g.UserId == userId))
            return false;

        var categoryIds = await db.Categories.ToDictionaryAsync(c => c.Name, c => c.Id);
        var thisMonth = new DateTime(today.Year, today.Month, 1);

        var plans = new (DateTime Month, MonthPlan Plan)[]
        {
            (thisMonth.AddMonths(-3), OldestMonth),
            (thisMonth.AddMonths(-2), MiddleMonth),
            (thisMonth.AddMonths(-1), LastMonth),
            (thisMonth, CurrentMonth)
        };

        var remainingByMonth = new Dictionary<DateTime, decimal>();
        foreach (var (month, plan) in plans)
        {
            var entries = BuildMonth(plan)
                .Where(e => e.Day <= DateTime.DaysInMonth(month.Year, month.Month))
                .Where(e => month < thisMonth || e.Day <= today.Day) // the current month only goes up to today
                .ToList();

            foreach (var e in entries)
            {
                db.Transactions.Add(new Transaction
                {
                    UserId = userId,
                    Type = e.Type,
                    Amount = e.Amount,
                    Date = new DateTime(month.Year, month.Month, e.Day),
                    CategoryId = categoryIds[e.Category],
                    ItemName = e.Item
                });
            }

            remainingByMonth[month] = entries.Sum(e => e.Type == TransactionType.Income ? e.Amount : -e.Amount);
        }

        var oldest = thisMonth.AddMonths(-3);
        var middle = thisMonth.AddMonths(-2);
        var goal = new Goal { UserId = userId, Name = GoalName, TargetAmount = GoalTarget, CreatedAt = oldest.AddDays(2) };
        db.Goals.Add(goal);
        await db.SaveChangesAsync(); // assigns goal.Id

        // The two oldest months were closed with a transfer to the goal; the last month is still waiting.
        foreach (var month in new[] { oldest, middle })
        {
            var remaining = remainingByMonth[month];
            db.MonthCloses.Add(new MonthClose
            {
                UserId = userId, Year = month.Year, Month = month.Month,
                Remaining = remaining, TransferredGoalId = remaining > 0 ? goal.Id : null
            });
            if (remaining > 0)
            {
                db.GoalContributions.Add(new GoalContribution
                {
                    GoalId = goal.Id, Amount = remaining, Date = month.AddMonths(1).AddDays(1),
                    Source = GoalContributionSource.MonthClose
                });
            }
        }

        // A manual "Para ekle" in the month that is still open: the carry-over card then offers only the rest
        // (291,02 - 150,00), which shows that the same money is not moved twice.
        db.GoalContributions.Add(new GoalContribution
        {
            GoalId = goal.Id, Amount = 150m, Date = thisMonth.AddMonths(-1).AddDays(9), Source = GoalContributionSource.Manual
        });

        await db.SaveChangesAsync();
        return true;
    }

    private static List<Entry> BuildMonth(MonthPlan plan)
    {
        var entries = new List<Entry>
        {
            // Income
            new(1, "Aile Desteği", TransactionType.Income, 3500m),
            new(5, "KYK Bursu", TransactionType.Income, 2000m),
            new(plan.PartTimeDay, "Part-time İş", TransactionType.Income, plan.PartTime),

            // Fixed expenses
            new(1, "Kira/Yurt", TransactionType.Expense, 3000m, "Yurt ücreti"),
            new(10, "Faturalar", TransactionType.Expense, 299m, "İnternet"),
            new(15, "Faturalar", TransactionType.Expense, plan.Electricity, "Elektrik faturası"),
            new(12, "Abonelikler", TransactionType.Expense, 189.99m, "Netflix"),
            new(14, "Abonelikler", TransactionType.Expense, 59.99m, "Spotify"),

            // Transport
            new(3, "Ulaşım", TransactionType.Expense, 200m, "İstanbulkart yükleme"),
            new(18, "Ulaşım", TransactionType.Expense, 200m, "İstanbulkart yükleme")
        };

        // Weekly grocery shopping: milk and bread come back every few days.
        (int Day, string Item, decimal Amount)[] groceries =
        [
            (2, "Süt", 42m), (2, "Yumurta", 85m), (6, "Ekmek", 18m), (9, "Makarna", 35m), (9, "Peynir", 120m),
            (13, "Süt", 42m), (13, "Ekmek", 18m), (16, "Tavuk", 165m), (20, "Süt", 42m), (20, "Ekmek", 18m),
            (23, "Pirinç", 70m), (27, "Süt", 42m), (27, "Ekmek", 18m), (27, "Zeytin", 95m)
        ];
        entries.AddRange(groceries.Select(g => new Entry(g.Day, "Market", TransactionType.Expense, g.Amount, g.Item)));

        entries.AddRange(plan.CoffeeDays.Select(d =>
            new Entry(d, "Kafe/Kahve", TransactionType.Expense, plan.CoffeePrice, "Latte")));

        entries.AddRange(plan.Extras);
        return entries;
    }
}
