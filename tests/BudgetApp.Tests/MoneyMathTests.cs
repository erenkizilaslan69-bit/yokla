using BudgetApp.Services;

namespace BudgetApp.Tests;

public class MoneyMathTests
{
    [Theory]
    [InlineData("12.345", "12.35")]
    [InlineData("12.344", "12.34")]
    [InlineData("0.005", "0.01")]
    [InlineData("0.004", "0.00")]
    [InlineData("-2.675", "-2.68")]
    [InlineData("100", "100")]
    public void Round_KeepsTheKurusAndRoundsHalvesAwayFromZero(string amount, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            MoneyMath.Round(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
}
