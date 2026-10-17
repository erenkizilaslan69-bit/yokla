using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class DemoQuickEntryParserTests
{
    private static readonly DateTime Today = new(2026, 10, 18);
    private static readonly QuickEntryContext Context = new(Today, ["Market"], ["KYK Bursu"]);

    private static (string Description, decimal? Amount, TransactionType Type, string? Category) Line(QuickEntryItem i) =>
        (i.Description, i.Amount, i.Type, i.Category);

    [Fact]
    public void Parse_SplitsTheExampleSentenceIntoThreeExpenses()
    {
        var result = DemoQuickEntryParser.Parse("simitçiye 20, otobüse 15, markete 230", Today);

        Assert.True(result.IsEntry);
        Assert.Equal(
        [
            ("Simit", 20m, TransactionType.Expense, "Dışarıda Yemek"),
            ("Otobüs", 15m, TransactionType.Expense, "Ulaşım"),
            ("Market alışverişi", 230m, TransactionType.Expense, "Market")
        ], result.Items.Select(Line));
        Assert.All(result.Items, i => Assert.Equal((QuickEntryConfidence.High, (DateTime?)null), (i.Confidence, i.Date)));
    }

    [Fact]
    public void Parse_ReadsTurkishNumbers()
    {
        var result = DemoQuickEntryParser.Parse("kahve 12,5; kira 1.250; kitap 2.5 ve fotokopi 1.250,75", Today);

        Assert.Equal([12.5m, 1250m, 2.5m, 1250.75m], result.Items.Select(i => i.Amount!.Value));
    }

    [Fact]
    public void Parse_KnowsSomeIncome()
    {
        var result = DemoQuickEntryParser.Parse("bursum yattı 3000, kuzenden 200 geldi", Today);

        Assert.Equal(
        [
            ("Burs", 3000m, TransactionType.Income, "KYK Bursu"),
            ("Kuzenden", 200m, TransactionType.Income, "Diğer Gelir")
        ], result.Items.Select(Line));
    }

    [Fact]
    public void Parse_YesterdayMovesEveryLine()
    {
        var result = DemoQuickEntryParser.Parse("dün simit 20, çay 10", Today);

        Assert.All(result.Items, i => Assert.Equal(Today.AddDays(-1), i.Date));
    }

    [Fact]
    public void Parse_AnUnknownThingIsAskedAbout()
    {
        var item = Assert.Single(DemoQuickEntryParser.Parse("kuyumcuya 500", Today).Items);

        Assert.Equal(("Kuyumcu", 500m, TransactionType.Expense, (string?)null), Line(item));
        Assert.Equal(QuickEntryConfidence.Medium, item.Confidence);
    }

    [Fact]
    public void Parse_AKnownThingWithoutAnAmountIsALowConfidenceLine()
    {
        var item = Assert.Single(DemoQuickEntryParser.Parse("otobüse bindim", Today).Items);

        Assert.Equal((null, QuickEntryConfidence.Low), (item.Amount, item.Confidence));
        Assert.NotNull(item.Issue);
    }

    [Fact]
    public void Parse_NoMoneyIsNotAnEntry()
    {
        var result = DemoQuickEntryParser.Parse("merhaba, nasılsın", Today);

        Assert.False(result.IsEntry);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ParseAsync_ARecordingAlwaysGivesTheSampleSentence()
    {
        var parser = new DemoQuickEntryParser();

        var result = await parser.ParseAsync(QuickEntryInput.FromAudio(new QuickEntryAudio([1, 2, 3], "audio/webm")), Context, default);

        Assert.Equal(DemoQuickEntryParser.SampleSentence, result.Heard);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(("demo", true), (parser.Provider, parser.IsDemo));
    }

    [Fact]
    public async Task ParseAsync_TypedTextIsReallySplit()
    {
        var result = await new DemoQuickEntryParser().ParseAsync(QuickEntryInput.FromText("çay 10"), Context, default);

        Assert.Equal(("Çay", 10m, TransactionType.Expense, "Kafe/Kahve"), Line(Assert.Single(result.Items)));
    }
}
