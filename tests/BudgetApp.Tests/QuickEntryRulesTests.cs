using System.Text;
using BudgetApp.Models;
using BudgetApp.Services;


namespace BudgetApp.Tests;

public class QuickEntryRulesTests
{
    private static readonly DateTime Today = new(2026, 10, 18);

    private static readonly List<Category> Categories =
    [
        new() { Id = 1, Name = "KYK Bursu", Type = TransactionType.Income },
        new() { Id = 4, Name = "Diğer Gelir", Type = TransactionType.Income },
        new() { Id = 5, Name = "Market", Type = TransactionType.Expense },
        new() { Id = 8, Name = "Ulaşım", Type = TransactionType.Expense },
        new() { Id = 13, Name = "Diğer Gider", Type = TransactionType.Expense },
        new() { Id = 14, Name = "Dışarıda Yemek", Type = TransactionType.Expense }
    ];

    private static QuickEntryItem Item(string description = "Simit", decimal? amount = 20m, TransactionType type = TransactionType.Expense,
        string? category = "Dışarıda Yemek", DateTime? date = null, QuickEntryConfidence confidence = QuickEntryConfidence.High,
        string? issue = null) => new(description, amount, type, category, date, confidence, issue);

    private static (List<QuickEntryLine> Lines, bool Auto) Run(string heard = "simitçiye 20", params QuickEntryItem[] items)
    {
        var lines = QuickEntryRules.Resolve(items, Categories, Today);
        return (lines, QuickEntryRules.CanAutoSave(heard, items, lines, Today));
    }

    // ---------- Resolve ----------

    [Fact]
    public void Resolve_TurnsClearItemsIntoLinesWithoutProblems()
    {
        var (lines, auto) = Run("simitçiye 20, otobüse 15, markete 230",
            Item(), Item("Otobüs", 15m, category: "Ulaşım"), Item("Market alışverişi", 230m, category: "Market"));

        Assert.True(auto);
        Assert.Equal([14, 8, 5], lines.Select(l => l.CategoryId));
        Assert.Equal([20m, 15m, 230m], lines.Select(l => l.Amount!.Value));
        Assert.All(lines, l => Assert.Equal((Today, (string?)null), (l.Date, l.Problem)));
    }

    [Fact]
    public void Resolve_RoundsTheAmountToTheKurus()
    {
        var (lines, _) = Run(items: Item(amount: 12.345m));
        Assert.Equal(12.35m, lines[0].Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Resolve_AMissingAmountIsLeftForTheUser(int? amount)
    {
        var (lines, auto) = Run(items: Item(amount: amount));

        Assert.False(auto);
        Assert.Null(lines[0].Amount);
        Assert.Contains("Tutarı duyamadım", lines[0].Problem);
    }

    [Fact]
    public void Resolve_MatchesTheCategoryIgnoringCaseTurkishStyle()
    {
        var (lines, auto) = Run(items: Item(category: "DIŞARIDA YEMEK"));

        Assert.True(auto);
        Assert.Equal(14, lines[0].CategoryId);
    }

    [Theory]
    [InlineData("Kuyumcu")]       // not a category
    [InlineData("KYK Bursu")]     // an income category on an expense
    [InlineData(null)]
    public void Resolve_AnUnusableCategoryBecomesTheCatchAllAndAsks(string? category)
    {
        var (lines, auto) = Run(items: Item(category: category));

        Assert.False(auto);
        Assert.Equal(13, lines[0].CategoryId); // Diğer Gider
        Assert.Contains("Kategoriyi seç", lines[0].Problem);
    }

    [Fact]
    public void Resolve_AnIncomeWithAnExpenseCategoryFallsBackToTheIncomeCatchAll()
    {
        var (lines, _) = Run(items: Item("Harçlık", 500m, TransactionType.Income, "Market"));

        Assert.Equal((TransactionType.Income, 4), (lines[0].Type, lines[0].CategoryId));
    }

    [Fact]
    public void Resolve_AFutureDateBecomesTodayAndIsPointedOut()
    {
        var (lines, auto) = Run(items: Item(date: Today.AddDays(1)));

        Assert.False(auto);
        Assert.Equal(Today, lines[0].Date);
        Assert.Contains("İleri bir tarih", lines[0].Problem);
    }

    [Fact]
    public void Resolve_KeepsAnOldDateButAsksAboutIt()
    {
        var (lines, auto) = Run(items: Item(date: Today.AddDays(-61)));

        Assert.False(auto);
        Assert.Equal(Today.AddDays(-61), lines[0].Date);
        Assert.Contains("60 günden eski", lines[0].Problem);
    }

    [Fact]
    public void Resolve_PassesOnWhatTheReaderDoubted()
    {
        var (lines, auto) = Run(items: Item(issue: "Tutar 15 mi 50 mi, emin olamadım."));

        Assert.False(auto);
        Assert.Equal("Tutar 15 mi 50 mi, emin olamadım.", lines[0].Problem);
    }

    [Theory]
    [InlineData(QuickEntryConfidence.Medium)]
    [InlineData(QuickEntryConfidence.Low)]
    public void Resolve_LowerConfidenceWithoutAReasonStillAsks(QuickEntryConfidence confidence)
    {
        var (lines, auto) = Run(items: Item(confidence: confidence));

        Assert.False(auto);
        Assert.Equal("Emin olamadım, kontrol et.", lines[0].Problem);
    }

    [Fact]
    public void Resolve_NamesTheLineAfterTheCategoryWhenTheReaderGaveNoName()
    {
        var (lines, _) = Run(items: Item(description: "  "));
        Assert.Equal("Dışarıda Yemek", lines[0].Description);
    }

    [Fact]
    public void Resolve_CapitalisesTheNameTurkishStyle()
    {
        var (lines, _) = Run(items: Item(description: "iskender"));
        Assert.Equal("İskender", lines[0].Description);
    }

    // ---------- CanAutoSave ----------

    [Fact]
    public void CanAutoSave_UpToEightLines()
    {
        Assert.True(Run(items: Enumerable.Repeat(Item(), 8).ToArray()).Auto);
        Assert.False(Run(items: Enumerable.Repeat(Item(), 9).ToArray()).Auto);
    }

    [Fact]
    public void CanAutoSave_DatesUpToSevenDaysBack()
    {
        Assert.True(Run(items: Item(date: Today.AddDays(-7))).Auto);
        Assert.False(Run(items: Item(date: Today.AddDays(-8))).Auto);
    }

    [Fact]
    public void CanAutoSave_NotWhenNothingWasHeard()
    {
        Assert.False(Run(" ", Item()).Auto);
    }

    [Fact]
    public void CanAutoSave_NotWithoutLines()
    {
        Assert.False(Run("merhaba").Auto);
    }

    [Fact]
    public void CanAutoSave_OneDoubtfulLineSendsAllToConfirmation()
    {
        Assert.False(Run(items: [Item(), Item("Otobüs", null, category: "Ulaşım")]).Auto);
    }

    // ---------- text ----------

    [Fact]
    public void Summary_ListsTheSavedLinesWithTheirAmounts()
    {
        var saved = new[]
        {
            new Transaction { ItemName = "Simit", Amount = 20m, Type = TransactionType.Expense },
            new Transaction { ItemName = "Harçlık", Amount = 500m, Type = TransactionType.Income }
        };

        Assert.Equal("Simit ₺20,00 · Harçlık +₺500,00", QuickEntryRules.Summary(saved));
    }

    // ---------- the recording's type ----------

    [Fact]
    public void DetectMimeType_KnowsTheBrowsersRecordings()
    {
        Assert.Equal("audio/webm", QuickEntryAudio.DetectMimeType([0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42]));
        Assert.Equal("audio/ogg", QuickEntryAudio.DetectMimeType("OggS\0\x02"u8));
        Assert.Equal("audio/wav", QuickEntryAudio.DetectMimeType("RIFF\x24\0\0\0WAVEfmt "u8));
    }

    [Theory]
    [InlineData("M4A ")]   // iPhone Voice Memos
    [InlineData("isom")]   // Safari's MediaRecorder
    [InlineData("mp42")]
    [InlineData("iso5")]
    public void DetectMimeType_SendsMp4AudioAsM4a(string brand) =>
        Assert.Equal("audio/m4a", QuickEntryAudio.DetectMimeType(Mp4(brand)));

    [Theory]
    [InlineData("heic")]
    [InlineData("mif1")]
    [InlineData("avif")]
    public void DetectMimeType_RefusesMp4FamilyPictures(string brand) =>
        Assert.Null(QuickEntryAudio.DetectMimeType(Mp4(brand)));

    [Fact]
    public void DetectMimeType_RefusesEverythingElse()
    {
        Assert.Null(QuickEntryAudio.DetectMimeType([]));
        Assert.Null(QuickEntryAudio.DetectMimeType([0xFF, 0xD8, 0xFF, 0xE0]));      // JPEG
        Assert.Null(QuickEntryAudio.DetectMimeType("simitçiye 20, otobüse 15"u8));  // text pretending
        Assert.Null(QuickEntryAudio.DetectMimeType("RIFF\x24\0\0\0WEBPVP8 "u8));    // a WebP picture
    }

    private static byte[] Mp4(string brand) => [0, 0, 0, 0x20, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(brand), 0, 0, 0, 0];

    // ---------- error messages ----------

    [Fact]
    public void ErrorMessagesAreTurkishSentencesForTheUser()
    {
        Assert.Equal("Bugünkü söyle ya da yaz hakkını kullandın (30/30). Yarın tekrar dene ya da işlemleri elle ekle.",
            new QuickEntryException(QuickEntryErrorKind.UserDailyLimit, limit: 30).Message);
        Assert.All(Enum.GetValues<QuickEntryErrorKind>(), kind => Assert.False(string.IsNullOrWhiteSpace(QuickEntryMessages.For(kind))));
        Assert.Equal(QuickEntryMessages.For(QuickEntryErrorKind.Misconfigured),
            new QuickEntryException(QuickEntryErrorKind.Misconfigured, "403 API key not valid").Message);
    }

    [Theory]
    [InlineData(GeminiFailure.Timeout, QuickEntryErrorKind.Timeout)]
    [InlineData(GeminiFailure.QuotaExceeded, QuickEntryErrorKind.QuotaExceeded)]
    [InlineData(GeminiFailure.Blocked, QuickEntryErrorKind.Blocked)]
    [InlineData(GeminiFailure.ServiceUnavailable, QuickEntryErrorKind.ServiceUnavailable)]
    public void KindOf_MapsTheReadersFailures(GeminiFailure failure, QuickEntryErrorKind expected) =>
        Assert.Equal(expected, QuickEntryException.KindOf(failure));
}
