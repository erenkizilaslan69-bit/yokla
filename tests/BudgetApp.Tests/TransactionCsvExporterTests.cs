using System.Globalization;
using System.Text;
using BudgetApp.Models;
using BudgetApp.Services;
using static BudgetApp.Tests.TestData;

namespace BudgetApp.Tests;

public class TransactionCsvExporterTests
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static DateTime Oct(int day) => new(2026, 10, day);

    private static TransactionCsvExporter Exporter(TestDb t) => new(new TransactionService(t.Context));

    private static async Task<byte[]> Export(TestDb t, string user = Alice, int year = 2026, int month = 10) =>
        await Exporter(t).ExportMonthAsync(user, year, month);

    /// <summary>The text after the byte order mark.</summary>
    private static string Text(byte[] bytes) => new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

    /// <summary>All lines (header first); the file ends with a line break, so the last empty piece is dropped.</summary>
    private static string[] Lines(byte[] bytes) => Text(bytes).Split("\r\n")[..^1];

    private static async Task<string[]> LinesOf(TestDb t) => Lines(await Export(t));

    private const string HeaderLine = "Tarih;Tür;Kategori;Ürün;Not;Tutar";

    // ---------- file format ----------

    [Fact]
    public async Task Export_StartsWithAUtf8ByteOrderMarkSoExcelReadsTurkishLetters()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3), Market, "Öğle yemeği");

        var bytes = await Export(t);

        Assert.Equal(Bom, bytes[..3]);
        Assert.NotEqual(Bom, bytes[3..6]); // the mark is written once
    }

    [Fact]
    public async Task Export_HasTheSixColumnsInTheAgreedOrder()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3));

        Assert.Equal(HeaderLine, (await LinesOf(t))[0]);
    }

    [Fact]
    public async Task Export_WritesOneSemicolonSeparatedLineWithCrLfPerTransaction()
    {
        using var t = new TestDb();
        var tx = AddExpense(t.Context, Alice, 1234.5m, Oct(7), Market, "Süt 1 L");
        tx.Note = "yarım yağlı";
        t.Context.SaveChanges();

        var bytes = await Export(t);

        Assert.Equal($"{HeaderLine}\r\n07.10.2026;Gider;Market;Süt 1 L;yarım yağlı;1234,50\r\n", Text(bytes));
    }

    [Fact]
    public async Task Export_LabelsIncomeAndExpenseAndKeepsTheAmountPositive()
    {
        using var t = new TestDb();
        AddIncome(t.Context, Alice, 2000m, Oct(5));
        AddExpense(t.Context, Alice, 3000m, Oct(1), Rent, "Yurt ücreti");

        var lines = await LinesOf(t);

        Assert.Equal("01.10.2026;Gider;Kira/Yurt;Yurt ücreti;;3000,00", lines[1]);
        Assert.Equal("05.10.2026;Gelir;KYK Bursu;;;2000,00", lines[2]);
    }

    // ---------- numbers and dates ----------

    [Theory]
    [InlineData("5", "5,00")]
    [InlineData("0.5", "0,50")]
    [InlineData("1234.5", "1234,50")]
    [InlineData("1234567.89", "1234567,89")]   // no thousands separator
    [InlineData("189.99", "189,99")]
    public async Task Export_AmountsUseADecimalCommaWithTwoDecimalsAndNoThousandsSeparator(string amount, string expected)
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, decimal.Parse(amount, CultureInfo.InvariantCulture), Oct(3));

        var line = (await LinesOf(t))[1];

        Assert.Equal(expected, line.Split(';')[^1]);
    }

    [Fact]
    public async Task Export_LooksTheSameWhateverCultureTheServerRunsIn()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 1234.5m, Oct(7), Market, "Süt");
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var american = await Export(t);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var german = await Export(t);

            Assert.Equal(american, german);
            Assert.Contains("07.10.2026;Gider;Market;Süt;;1234,50", Text(american));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ---------- which rows, in which order ----------

    [Fact]
    public async Task Export_ListsTheOldestTransactionFirst()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 3m, Oct(20), Market, "üçüncü");
        AddExpense(t.Context, Alice, 1m, Oct(2), Market, "birinci");
        AddExpense(t.Context, Alice, 2m, Oct(9), Market, "ikinci");

        var items = (await LinesOf(t)).Skip(1).Select(l => l.Split(';')[3]).ToArray();

        Assert.Equal(["birinci", "ikinci", "üçüncü"], items);
    }

    [Fact]
    public async Task Export_KeepsTheOrderOfEntriesOnTheSameDay()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 1m, Oct(5), Market, "a");
        AddExpense(t.Context, Alice, 2m, Oct(5), Market, "b");
        AddExpense(t.Context, Alice, 3m, Oct(5), Market, "c");

        Assert.Equal(["a", "b", "c"], (await LinesOf(t)).Skip(1).Select(l => l.Split(';')[3]).ToArray());
    }

    [Fact]
    public async Task Export_ContainsOnlyTheRequestedMonthIncludingItsFirstAndLastDay()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 1m, new DateTime(2026, 9, 30), Market, "eylül");
        AddExpense(t.Context, Alice, 2m, Oct(1), Market, "ilk gün");
        AddExpense(t.Context, Alice, 3m, Oct(31), Market, "son gün");
        AddExpense(t.Context, Alice, 4m, new DateTime(2026, 11, 1), Market, "kasım");

        var items = (await LinesOf(t)).Skip(1).Select(l => l.Split(';')[3]).ToArray();

        Assert.Equal(["ilk gün", "son gün"], items);
    }

    [Fact]
    public async Task Export_ContainsOnlyTheUsersOwnTransactions()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 1m, Oct(3), Market, "benim");
        AddExpense(t.Context, Bob, 2m, Oct(3), Market, "bobun");

        var lines = await LinesOf(t);

        Assert.Equal(2, lines.Length); // header + her own row
        Assert.DoesNotContain("bobun", string.Join('\n', lines));

        var bobs = Lines(await Export(t, Bob));
        Assert.Equal(2, bobs.Length);
        Assert.Contains("bobun", bobs[1]);
        Assert.DoesNotContain("benim", string.Join('\n', bobs));
    }

    [Fact]
    public async Task Export_AnEmptyMonthIsJustTheHeader()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 1m, new DateTime(2026, 9, 3), Market);

        var bytes = await Export(t);

        Assert.Equal(Bom, bytes[..3]);
        Assert.Equal($"{HeaderLine}\r\n", Text(bytes));
    }

    [Fact]
    public async Task Export_IncludesTransactionsThatWereGeneratedFromRecurringDefinitions()
    {
        using var t = new TestDb();
        AddGeneratedExpense(t.Context, Alice, 299m, Oct(10), Bills, "İnternet");

        Assert.Equal("10.10.2026;Gider;Faturalar;İnternet;;299,00", (await LinesOf(t))[1]);
    }

    // ---------- text ----------

    [Fact]
    public async Task Export_KeepsTurkishLettersAsUtf8()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3), EatingOut, "Öğle yemeği çorbası İSTANBUL ışık");

        var bytes = await Export(t);

        Assert.Contains("Dışarıda Yemek;Öğle yemeği çorbası İSTANBUL ışık", Text(bytes));
        Assert.Contains((byte)0xC4, bytes);   // lead byte of ğ, ş, ı, İ in UTF-8 (not a single-byte code page)
    }

    [Fact]
    public async Task Export_LeavesMissingItemAndNoteEmpty()
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3), Market);

        Assert.Equal("03.10.2026;Gider;Market;;;10,00", (await LinesOf(t))[1]);
    }

    [Theory]
    [InlineData("kahve; süt", "\"kahve; süt\"")]                 // separator
    [InlineData("3\" boru", "\"3\"\" boru\"")]                    // quote is doubled
    [InlineData("iki\nsatır", "\"iki\nsatır\"")]                  // line break
    [InlineData("iki\r\nsatır", "\"iki\r\nsatır\"")]
    [InlineData("düz yazı, virgüllü", "düz yazı, virgüllü")]     // a comma is not a separator here
    public async Task Export_QuotesFieldsThatWouldBreakTheRow(string item, string expectedField)
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3), Market, item);

        Assert.Contains($"03.10.2026;Gider;Market;{expectedField};;10,00\r\n", Text(await Export(t)));
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+90 555", "'+90 555")]
    [InlineData("-5 indirim", "'-5 indirim")]
    [InlineData("@kullanici", "'@kullanici")]
    [InlineData("=HYPERLINK(\"http://x\";\"tıkla\")", "\"'=HYPERLINK(\"\"http://x\"\";\"\"tıkla\"\")\"")]
    public async Task Export_DefusesFieldsAnySpreadsheetWouldRunAsAFormula(string item, string expectedField)
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3), Market, item);

        Assert.Contains($";Market;{expectedField};;10,00\r\n", Text(await Export(t)));
    }

    [Theory]
    [InlineData("Süt")]
    [InlineData("5 - 3")]
    [InlineData("3x ekmek")]
    [InlineData("a=b")]
    public async Task Export_DoesNotTouchOrdinaryText(string item)
    {
        using var t = new TestDb();
        AddExpense(t.Context, Alice, 10m, Oct(3), Market, item);

        Assert.Contains($";Market;{item};;10,00\r\n", Text(await Export(t)));
    }

    [Fact]
    public async Task Export_DefusesFormulasInTheNoteToo()
    {
        using var t = new TestDb();
        var tx = AddExpense(t.Context, Alice, 10m, Oct(3), Market, "Süt");
        tx.Note = "=cmd|' /C calc'!A0";
        t.Context.SaveChanges();

        var line = (await LinesOf(t))[1];

        Assert.StartsWith("03.10.2026;Gider;Market;Süt;'=cmd|' /C calc'!A0;10,00", line);
    }
}
