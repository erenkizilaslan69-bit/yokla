namespace BudgetApp.Services;

/// <summary>"Receipts" section of the configuration.</summary>
public sealed class ReceiptOptions
{
    public const string Section = "Receipts";

    /// <summary>Receipts one user may send to the reader per day.</summary>
    public int DailyLimitPerUser { get; set; } = 10;

    /// <summary>
    /// Requests the whole app may send to the reader per day, receipts and quick entries together: the provider's
    /// free quota belongs to the one API key.
    /// </summary>
    public int DailyLimitTotal { get; set; } = 300;

    public int MaxUploadBytes { get; set; } = 4_000_000;

    /// <summary>The browser shrinks a picture so that its longer side is at most this many pixels.</summary>
    public int MaxImageEdge { get; set; } = 2048;
}
