using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BudgetApp.Infrastructure;

/// <summary>
/// The instant warnings travel from the request that saved something to the transactions list in TempData. Saving a
/// transaction, a receipt or a quick entry all leave them here; the list takes them once.
/// </summary>
public static class WarningsFlash
{
    private const string Key = "Warnings";

    public static void Set(ITempDataDictionary tempData, IReadOnlyCollection<string> messages)
    {
        if (messages.Count > 0)
            tempData[Key] = JsonSerializer.Serialize(messages);
    }

    public static List<string> Take(ITempDataDictionary tempData) =>
        tempData[Key] is string json ? JsonSerializer.Deserialize<List<string>>(json) ?? [] : [];
}
