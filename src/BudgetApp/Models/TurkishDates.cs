using System.Globalization;

namespace BudgetApp.Models;

/// <summary>Dates as words on the pages: "Bugün", "Dün", "10 Ekim Cuma", "27 Ekim'de".</summary>
public static class TurkishDates
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>The locative of each month ("Ekim'de", "Ocak'ta"): the suffix follows the month's last sound.</summary>
    private static readonly string[] MonthLocatives =
    [
        "Ocak'ta", "Şubat'ta", "Mart'ta", "Nisan'da", "Mayıs'ta", "Haziran'da",
        "Temmuz'da", "Ağustos'ta", "Eylül'de", "Ekim'de", "Kasım'da", "Aralık'ta"
    ];

    /// <summary>The heading of a day in a list: "Bugün", "Dün", "10 Ekim Cuma"; another year adds the year.</summary>
    public static string DayHeading(DateTime date, DateTime today)
    {
        var day = date.Date;
        if (day == today.Date) return "Bugün";
        if (day == today.Date.AddDays(-1)) return "Dün";
        return day.Year == today.Year
            ? day.ToString("d MMMM dddd", Tr)
            : day.ToString("d MMMM yyyy dddd", Tr);
    }

    /// <summary>"On that day" in a sentence: "bugün", "yarın", "27 Ekim'de" (another year: "3 Ocak'ta (2027)").</summary>
    public static string OnDay(DateTime date, DateTime today)
    {
        var day = date.Date;
        if (day == today.Date) return "bugün";
        if (day == today.Date.AddDays(1)) return "yarın";
        var text = $"{day.Day} {MonthLocatives[day.Month - 1]}";
        return day.Year == today.Year ? text : $"{text} ({day.Year})";
    }
}
