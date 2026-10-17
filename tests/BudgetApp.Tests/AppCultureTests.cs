using System.Globalization;
using BudgetApp.Localization;

namespace BudgetApp.Tests;

public class AppCultureTests
{
    private const char Minus = '−';

    [Fact]
    public void Money_WritesNegativeAmountsWithTheRealMinusSign()
    {
        Assert.Equal("−₺729,53", AppCulture.Money(-729.53m));
        Assert.Equal(Minus, AppCulture.Money(-1m)[0]);
        Assert.DoesNotContain("-", AppCulture.Money(-1234.5m));
    }

    [Fact]
    public void Money_LeavesPositiveAmountsAndZeroAsTheyWere()
    {
        Assert.Equal("₺9.500,00", AppCulture.Money(9500m));
        Assert.Equal("₺0,00", AppCulture.Money(0m));
    }

    [Fact]
    public void SignedMoney_AlwaysWritesTheSign()
    {
        Assert.Equal("+₺1.300,00", AppCulture.SignedMoney(1300m));
        Assert.Equal("−₺120,00", AppCulture.SignedMoney(-120m));
        Assert.Equal("₺0,00", AppCulture.SignedMoney(0m));
    }

    [Fact]
    public void Turkish_IsTrTrWithOnlyTheMinusSignChanged()
    {
        var culture = AppCulture.Turkish;

        Assert.Equal("tr-TR", culture.Name);
        Assert.True(culture.IsReadOnly);
        Assert.Equal("−50", (-50m).ToString("N0", culture));
        Assert.Equal("−12,5", (-12.5m).ToString(culture));
        // everything else is plain Turkish
        Assert.Equal("1.234,50", 1234.5m.ToString("N2", culture));
        Assert.Equal("17 Ekim 2026 Cumartesi", new DateTime(2026, 10, 17).ToString("d MMMM yyyy dddd", culture));
        Assert.Equal(1234.5m, decimal.Parse("1.234,50", culture)); // what the forms send still binds
        // the stock culture is not touched
        Assert.Equal("-", CultureInfo.GetCultureInfo("tr-TR").NumberFormat.NegativeSign);
    }
}
