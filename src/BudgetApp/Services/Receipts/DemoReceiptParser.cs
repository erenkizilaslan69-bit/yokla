using System.Security.Cryptography;

namespace BudgetApp.Services;

/// <summary>
/// Development stand-in for a real reader when no API key is set: whatever picture comes in, one of a few
/// ready-made receipts comes out (always the same one for the same picture). The shops are made up.
/// </summary>
public sealed class DemoReceiptParser(TimeProvider? timeProvider = null) : IReceiptParser
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public string Provider => "demo";

    public bool IsDemo => true;

    /// <summary>How many ready-made results there are.</summary>
    public const int SampleCount = 4;

    public Task<ReceiptParseResult> ParseAsync(ReceiptImage image, IReadOnlyList<string> expenseCategories, CancellationToken cancellationToken)
    {
        var today = _time.GetLocalNow().Date;
        var sample = SHA256.HashData(image.Data)[0] % SampleCount;
        return Task.FromResult(Sample(sample, today));
    }

    /// <summary>The ready-made results: a plain grocery receipt, one with a discount on the whole receipt, a café
    /// receipt whose lines do not add up to its total, and a picture that is not a receipt.</summary>
    public static ReceiptParseResult Sample(int index, DateTime today) => index switch
    {
        0 => new ReceiptParseResult(true, "Köşe Market", today.AddDays(-1), 253.02m, 0m,
        [
            new("SUT YRM YGL 1L", "Yarım yağlı süt 1 L", 1m, "adet", 42.50m, "Market"),
            new("EKMEK TAM BUGDAY", "Tam buğday ekmeği", 1m, "adet", 22.00m, "Market"),
            new("YUMURTA 15LI", "Yumurta (15'li)", 1m, "adet", 89.90m, "Market"),
            new("DOMATES 0,845 KG X 39,90", "Domates", 0.845m, "kg", 33.72m, "Market"),
            new("DTRJN BULASIK 750ML", "Bulaşık deterjanı 750 ml", 1m, "adet", 64.90m, "Market")
        ]),
        1 => new ReceiptParseResult(true, "Yeşil Yaprak Gross", today, 402.90m, 20.00m,
        [
            new("BEYAZ PEYNIR 500G", "Beyaz peynir 500 g", 1m, "adet", 119.90m, "Market"),
            new("ZEYTIN SIYAH GEMLIK", "Siyah zeytin (Gemlik)", 1m, "adet", 84.50m, "Market"),
            new("MAKARNA 500G 2 X 19,75", "Makarna 500 g", 2m, "adet", 39.50m, "Market"),
            new("CAY 1KG", "Çay 1 kg", 1m, "adet", 179.00m, "Market")
        ]),
        2 => new ReceiptParseResult(true, "Köşe Kafe", today, 165.00m, 0m,
        [
            new("LATTE BYK", "Latte (büyük)", 1m, "adet", 95.00m, "Kafe/Kahve"),
            new("SU 0,5L", "Su 0,5 L", 1m, "adet", 15.00m, "Kafe/Kahve"),
            new("KURABIYE", "Kurabiye", 1m, "adet", 45.00m, "Kafe/Kahve")
        ]),
        _ => new ReceiptParseResult(false, null, null, null, 0m, [])
    };
}
