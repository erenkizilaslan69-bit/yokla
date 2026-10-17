using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class ReaderUsageTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    private static void Add(TestDb t, string user, ScanKind kind, string provider = "gemini",
        ReceiptScanOutcome outcome = ReceiptScanOutcome.Succeeded, DateTime? at = null) =>
        t.Context.ReceiptScans.Add(new ReceiptScan { UserId = user, Kind = kind, Provider = provider, Outcome = outcome, CreatedAt = at ?? Now });

    [Fact]
    public async Task CountTodayAsync_CountsTheUsersKindAndEveryonesAttempts()
    {
        using var t = new TestDb();
        Add(t, "alice", ScanKind.Receipt);
        Add(t, "alice", ScanKind.QuickEntry);
        Add(t, "alice", ScanKind.QuickEntry);
        Add(t, "alice", ScanKind.QuickEntry, outcome: ReceiptScanOutcome.Unanswered); // not the user's fault
        Add(t, "bob", ScanKind.QuickEntry);
        Add(t, "alice", ScanKind.QuickEntry, provider: "demo");                         // free
        Add(t, "alice", ScanKind.QuickEntry, at: Now.Date.AddSeconds(-1));               // yesterday
        t.Context.SaveChanges();

        Assert.Equal((2, 5), await ReaderUsage.CountTodayAsync(t.Context, "alice", ScanKind.QuickEntry, Now));
        Assert.Equal((1, 5), await ReaderUsage.CountTodayAsync(t.Context, "alice", ScanKind.Receipt, Now));
    }
}
