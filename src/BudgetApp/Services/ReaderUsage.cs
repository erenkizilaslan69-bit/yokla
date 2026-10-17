using BudgetApp.Data;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Services;

/// <summary>Today's attempts at the reader (Gemini), for the daily limits of receipt reading and quick entry.</summary>
public static class ReaderUsage
{
    /// <summary>
    /// User: the user's attempts of this kind that the reader answered (or is still answering). Total: every attempt
    /// of every kind by anyone, because the provider may have spent quota even on one that timed out on our side.
    /// Demo attempts count for neither.
    /// </summary>
    public static async Task<(int User, int Total)> CountTodayAsync(ApplicationDbContext db, string userId, ScanKind kind, DateTime now)
    {
        var start = now.Date;
        var end = start.AddDays(1);
        var today = db.ReceiptScans.AsNoTracking().Where(s => s.CreatedAt >= start && s.CreatedAt < end && s.Provider != "demo");
        return (await today.CountAsync(s => s.UserId == userId && s.Kind == kind && s.Outcome != ReceiptScanOutcome.Unanswered),
                await today.CountAsync());
    }
}
