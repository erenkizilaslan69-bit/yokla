using BudgetApp.Services;

namespace BudgetApp.Tests;

public class DemoReceiptParserTests
{
    private static readonly string[] Categories = ["Market", "Kafe/Kahve", "Diğer Gider"];

    [Fact]
    public async Task ParseAsync_AlwaysGivesTheSameResultForTheSamePicture()
    {
        var parser = new DemoReceiptParser();
        var image = new ReceiptImage([0xFF, 0xD8, 0xFF, 1, 2, 3], "image/jpeg");

        var first = await parser.ParseAsync(image, Categories, default);
        var second = await parser.ParseAsync(image, Categories, default);

        Assert.Equal(first.StoreName, second.StoreName);
        Assert.Equal(first.Lines, second.Lines);
    }

    [Fact]
    public async Task ParseAsync_DifferentPicturesReachEverySample()
    {
        var parser = new DemoReceiptParser();
        var seen = new HashSet<string>();
        for (byte i = 0; i < 64 && seen.Count < DemoReceiptParser.SampleCount; i++)
        {
            var result = await parser.ParseAsync(new ReceiptImage([0xFF, 0xD8, 0xFF, i], "image/jpeg"), Categories, default);
            seen.Add(result.StoreName ?? "(not a receipt)");
        }

        Assert.Equal(DemoReceiptParser.SampleCount, seen.Count);
    }

    [Fact]
    public void Samples_CoverTheCasesTheConfirmationScreenHasToShow()
    {
        var today = new DateTime(2026, 10, 16);
        var samples = Enumerable.Range(0, DemoReceiptParser.SampleCount).Select(i => DemoReceiptParser.Sample(i, today)).ToList();

        // a plain receipt that adds up
        Assert.Equal(samples[0].Total, samples[0].Lines.Sum(l => l.Amount));
        // a discount on the whole receipt
        Assert.Equal(samples[1].Total, samples[1].Lines.Sum(l => l.Amount) - samples[1].GeneralDiscount);
        Assert.True(samples[1].GeneralDiscount > 0);
        // lines that do not add up to the total
        Assert.NotEqual(samples[2].Total, samples[2].Lines.Sum(l => l.Amount));
        // not a receipt
        Assert.False(samples[3].IsReceipt);

        foreach (var sample in samples.Where(s => s.IsReceipt))
        {
            Assert.True(sample.Date <= today);
            Assert.All(sample.Lines, l =>
            {
                Assert.True(l.Amount > 0);
                Assert.False(string.IsNullOrWhiteSpace(l.Name));
                Assert.Contains(l.Category, Categories);
            });
        }
    }

    [Fact]
    public void IsADemo() =>
        Assert.Equal(("demo", true), (new DemoReceiptParser().Provider, new DemoReceiptParser().IsDemo));
}
