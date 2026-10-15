namespace BudgetApp.Models;

public enum ReceiptScanOutcome
{
    /// <summary>Sent to the reader, no answer yet (or the request died on the way).</summary>
    Pending,
    Succeeded,
    Failed
}

/// <summary>
/// One attempt to read a receipt. Only the attempt is kept, for the daily limits: neither the picture nor what was
/// read from it is ever stored.
/// </summary>
public class ReceiptScan
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Which reader handled it ("gemini", "demo"); demo attempts do not count towards the limits.</summary>
    public string Provider { get; set; } = string.Empty;

    public ReceiptScanOutcome Outcome { get; set; }
}
