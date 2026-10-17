namespace BudgetApp.Models;

public enum ReceiptScanOutcome
{
    /// <summary>Sent to the reader, no answer yet (or the request died on the way).</summary>
    Pending,
    Succeeded,
    /// <summary>The reader answered, but there was no usable receipt (not a receipt, unusable answer, blocked).</summary>
    Failed,
    /// <summary>
    /// The reader never answered: timeout, network or server error, rate or quota limit, configuration error.
    /// Not the user's fault, so it does not use up the user's daily allowance; it still counts towards the app's
    /// limit, because the provider may have spent quota on it.
    /// </summary>
    Unanswered
}

/// <summary>What was sent to the reader. Each kind has its own daily allowance per user; the app's limit counts both.</summary>
public enum ScanKind
{
    Receipt = 0,
    /// <summary>"Söyle ya da yaz": a voice recording or a typed sentence turned into transactions.</summary>
    QuickEntry = 1
}

/// <summary>
/// One attempt to read a receipt or a quick entry. Only the attempt is kept, for the daily limits: neither the
/// picture or recording nor what was read from it is ever stored.
/// </summary>
public class ReceiptScan
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Which reader handled it ("gemini", "demo"); demo attempts do not count towards the limits.</summary>
    public string Provider { get; set; } = string.Empty;

    public ReceiptScanOutcome Outcome { get; set; }

    public ScanKind Kind { get; set; } = ScanKind.Receipt;
}
