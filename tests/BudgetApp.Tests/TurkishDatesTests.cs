using BudgetApp.Models;

namespace BudgetApp.Tests;

public class TurkishDatesTests
{
    private static readonly DateTime Today = new(2026, 10, 17); // a Saturday

    [Theory]
    [InlineData(2026, 10, 17, "Bugün")]
    [InlineData(2026, 10, 16, "Dün")]
    [InlineData(2026, 10, 10, "10 Ekim Cumartesi")]
    [InlineData(2026, 9, 1, "1 Eylül Salı")]
    [InlineData(2025, 12, 31, "31 Aralık 2025 Çarşamba")]
    public void DayHeading_NamesTodayYesterdayAndOtherDays(int year, int month, int day, string expected) =>
        Assert.Equal(expected, TurkishDates.DayHeading(new DateTime(year, month, day, 14, 30, 0), Today));

    [Theory]
    [InlineData(2026, 10, 17, "bugün")]
    [InlineData(2026, 10, 18, "yarın")]
    [InlineData(2026, 10, 27, "27 Ekim'de")]
    [InlineData(2026, 11, 3, "3 Kasım'da")]
    [InlineData(2026, 12, 1, "1 Aralık'ta")]
    [InlineData(2027, 1, 5, "5 Ocak'ta (2027)")]
    public void OnDay_PutsTheRightSuffixOnTheMonth(int year, int month, int day, string expected) =>
        Assert.Equal(expected, TurkishDates.OnDay(new DateTime(year, month, day), Today));

    [Fact]
    public void OnDay_HasASuffixForEveryMonth()
    {
        var expected = new[] { "Ocak'ta", "Şubat'ta", "Mart'ta", "Nisan'da", "Mayıs'ta", "Haziran'da",
                               "Temmuz'da", "Ağustos'ta", "Eylül'de", "Ekim'de", "Kasım'da", "Aralık'ta" };
        for (var m = 1; m <= 12; m++)
            Assert.Equal($"15 {expected[m - 1]}", TurkishDates.OnDay(new DateTime(2026, m, 15), new DateTime(2026, 1, 1)));
    }
}
