using System.Globalization;

namespace BudgetApp.Localization;

/// <summary>
/// The one place numbers and money are formatted from. It is the tr-TR culture with a single change: negative
/// numbers carry the real minus sign (−, U+2212) instead of the hyphen-minus (-) .NET uses for Turkish, so
/// "−₺729,53" lines up with "+₺9.500,00" and does not read as a dash. The app's request culture is this culture,
/// so every <c>ToString("C2")</c> in a view picks it up; services format their sentences with it too.
/// </summary>
public static class AppCulture
{
    public const string MinusSign = "−";

    public static CultureInfo Turkish { get; } = Create();

    private static CultureInfo Create()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("tr-TR").Clone();
        culture.NumberFormat.NegativeSign = MinusSign;
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>An amount as the pages show it: "₺1.234,50", "−₺729,53".</summary>
    public static string Money(decimal value) => value.ToString("C2", Turkish);

    /// <summary>An amount with its sign always written: "+₺1.300,00", "−₺120,00", "₺0,00".</summary>
    public static string SignedMoney(decimal value) => value > 0 ? "+" + Money(value) : Money(value);
}
