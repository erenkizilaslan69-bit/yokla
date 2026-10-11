using System.Security.Claims;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BudgetApp.Infrastructure;

/// <summary>
/// There is no background job: before every MVC action of a signed-in user, the recurring occurrences that are
/// due are generated. This keeps every page (dashboard, list, analysis, month close) on up-to-date data without
/// each controller having to remember it. The service call is idempotent and costs one small query.
/// </summary>
public class RecurringGenerationFilter(IRecurringTransactionService recurring) : IAsyncActionFilter
{
    /// <summary>TempData key read by the _Flash partial.</summary>
    public const string FlashKey = "Recurring";

    private const int NamedInMessage = 3;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
        {
            var created = await recurring.GenerateDueAsync(userId, DateTime.Today);
            if (created.Count > 0 && context.Controller is Controller controller)
                controller.TempData[FlashKey] = Describe(created);
        }

        await next();
    }

    /// <summary>Instead of per-transaction warnings, the user gets one note saying what was added.</summary>
    private static string Describe(List<Transaction> created)
    {
        var named = created.Take(NamedInMessage).Select(t =>
        {
            var name = string.IsNullOrWhiteSpace(t.ItemName) ? t.Category?.Name : t.ItemName;
            var sign = t.Type == TransactionType.Income ? "+" : "−";
            return $"{name} ({sign}{t.Amount:C2})";
        });

        var text = created.Count == 1
            ? "Tekrarlayan işlem eklendi: "
            : $"{created.Count} tekrarlayan işlem eklendi: ";
        text += string.Join(", ", named);
        if (created.Count > NamedInMessage)
            text += $" ve {created.Count - NamedInMessage} işlem daha";
        return text + ".";
    }
}
