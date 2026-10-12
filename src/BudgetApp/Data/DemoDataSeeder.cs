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

    /// <summary>Monthly limits of the demo user, by category name.</summary>
    private static readonly (string Category, decimal Limit)[] Budgets =
    [
        ("Kafe/Kahve", 500m),
        ("Dışarıda Yemek", 600m),
        ("Eğlence", 400m),
        ("Market", 1500m)
    ];

    /// <summary>One planned transaction; <see cref="Day"/> is the day of the month it happens on.</summary>
    /// <param name="Recurring">Key of the recurring definition this entry is an occurrence of, if any.</param>
    private record Entry(int Day, string Category, TransactionType Type, decimal Amount, string? Item = null, string? Recurring = null);

    /// <summary>A monthly recurring definition of the demo user. Amounts are the "usual" ones; single months may differ.</summary>
    private record Definition(string Key, string Category, TransactionType Type, decimal Amount, int Day, string? Item = null);

    private static readonly Definition[] Definitions =
    [
        new("family", "Aile Desteği", TransactionType.Income, 3500m, 1),
        new("scholarship", "KYK Bursu", TransactionType.Income, 2000m, 5),
        new("part-time", "Part-time İş", TransactionType.Income, 1500m, 20),
        new("rent", "Kira/Yurt", TransactionType.Expense, 3000m, 1, "Yurt ücreti"),
        new("internet", "Faturalar", TransactionType.Expense, 299m, 10, "İnternet"),
        new("electricity", "Faturalar", TransactionType.Expense, 300m, 15, "Elektrik faturası"),
        new("netflix", "Abonelikler", TransactionType.Expense, 189.99m, 12, "Netflix"),
        new("spotify", "Abonelikler", TransactionType.Expense, 59.99m, 14, "Spotify")
    ];

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
            // After the part-time pay, not in the tight first days: the month keeps its weekly allowances and gets
            // the best report card of the three.
            Edu(22, "Ders kitabı", 200m)
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
        // Tuned against the demo budgets below, so that from about the 10th to the 18th of the month the analysis page
        // shows all three bar colours: eating out is at 85% of 600 (gold), entertainment is over its 400 (red),
        // groceries stay far below 1.500 (green). Coffee moves from green to gold to red as the month goes on.
        Extras:
        [
            Out(3, "Döner", 160m), Out(7, "Pizza", 220m), Out(10, "Öğle yemeği", 130m), Out(19, "Döner", 150m),
            Fun(6, "Sinema bileti", 150m), Fun(9, "Steam oyunu", 300m),
            Edu(9, "Fotokopi", 45m),
            // A one-off income. Without it the month's heavy spending leaves almost nothing until the part-time
            // pay of the 20th and the daily allowance card would show a few kuruş; with it the card shows a
            // believable amount (about 100 TL a day around the middle of the month).
            new Entry(8, "Diğer Gelir", TransactionType.Income, 800m, "Özel ders")
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
    /// the recurring definitions behind the monthly items, category budgets, a goal and the month-close records of
    /// the two oldest months. Returns false if the user already has data.
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

        // Rent, scholarship, subscriptions... are recurring definitions and their monthly transactions are linked
        // to them, exactly as if they had been generated. A definition counts as handled for the current month
        // once its day has come; the rest is generated by the app on the day it is due.
        var oldestMonth = thisMonth.AddMonths(-3);
        var definitions = Definitions.ToDictionary(d => d.Key, d => new RecurringTransaction
        {
            UserId = userId,
            Type = d.Type,
            Amount = d.Amount,
            CategoryId = categoryIds[d.Category],
            ItemName = d.Item,
            DayOfMonth = d.Day,
            StartDate = oldestMonth,
            IsActive = true,
            LastGeneratedPeriod = d.Day <= today.Day ? thisMonth : thisMonth.AddMonths(-1),
            CreatedAt = oldestMonth
        });
        db.RecurringTransactions.AddRange(definitions.Values);

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
                    ItemName = e.Item,
                    RecurringTransaction = e.Recurring is null ? null : definitions[e.Recurring],
                    RecurringPeriod = e.Recurring is null ? null : month
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

        // Category limits. No warning state is stored, so the next expense in an over-limit category still shows
        // the budget warning: a ready-made demo of that rule.
        foreach (var (category, limit) in Budgets)
            db.CategoryBudgets.Add(new CategoryBudget { UserId = userId, CategoryId = categoryIds[category], MonthlyLimit = limit });

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
            // Income (each one is an occurrence of a recurring definition; the part-time pay varies a little)
            new(1, "Aile Desteği", TransactionType.Income, 3500m, Recurring: "family"),
            new(5, "KYK Bursu", TransactionType.Income, 2000m, Recurring: "scholarship"),
            new(plan.PartTimeDay, "Part-time İş", TransactionType.Income, plan.PartTime, Recurring: "part-time"),

            // Fixed expenses (recurring too; the electricity bill differs from month to month)
            new(1, "Kira/Yurt", TransactionType.Expense, 3000m, "Yurt ücreti", "rent"),
            new(10, "Faturalar", TransactionType.Expense, 299m, "İnternet", "internet"),
            new(15, "Faturalar", TransactionType.Expense, plan.Electricity, "Elektrik faturası", "electricity"),
            new(12, "Abonelikler", TransactionType.Expense, 189.99m, "Netflix", "netflix"),
            new(14, "Abonelikler", TransactionType.Expense, 59.99m, "Spotify", "spotify"),

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
