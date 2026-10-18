using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace BudgetApp.Infrastructure;

/// <summary>
/// Which pages of the Identity area can be reached. The Identity UI package also brings account management, password
/// reset, e-mail confirmation, two-factor and "delete personal data" pages; they are in English, outside the app's
/// design, and "delete personal data" would remove only the login while the user's transactions stay. Until the app
/// has its own versions, those pages get no route and answer 404.
/// </summary>
public static class IdentityPages
{
    /// <summary>The pages the app replaces with its own (Areas/Identity/Pages/Account).</summary>
    public static readonly IReadOnlySet<string> Open = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/Account/Login", "/Account/Register", "/Account/Logout"
    };

    public static void CloseAllExceptOwn(PageConventionCollection conventions) =>
        conventions.AddAreaFolderRouteModelConvention("Identity", "/", model =>
        {
            if (!Open.Contains(model.ViewEnginePath))
                model.Selectors.Clear();
        });
}
