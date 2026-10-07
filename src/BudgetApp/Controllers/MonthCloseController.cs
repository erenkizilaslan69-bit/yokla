using System.Security.Claims;
using BudgetApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Controllers;

[Authorize]
public class MonthCloseController(IMonthCloseService monthCloses) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int year, int month, bool transfer)
    {
        try
        {
            var close = await monthCloses.CloseAsync(UserId, year, month, transfer, DateTime.Today);
            TempData["Message"] = close.TransferredGoalId is not null
                ? "Kalan tutar hedefe aktarıldı."
                : "Ay kapatıldı.";
        }
        catch (InvalidOperationException)
        {
            TempData["Message"] = "Bu ay zaten kapatılmış.";
        }
        catch (ArgumentException)
        {
            return BadRequest();
        }

        return RedirectToAction("Index", "Home");
    }
}
