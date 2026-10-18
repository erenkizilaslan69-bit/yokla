using System.Net;

namespace BudgetApp.Tests.Web;

/// <summary>Scripts load where they are needed, in the order they need, and every file a page asks for exists.</summary>
public class PageAssetsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task JQueryLoadsOnlyWithClientSideValidationAndBeforeIt()
    {
        var login = await app.Browser().GetStringAsync("/Identity/Account/Login");
        // File names carry a fingerprint (jquery.min.o1o13a6vjx.js), so only their start is matched. The Identity pages
        // must use the app's scripts, not the Identity package's (/Identity/lib/..., which has no jQuery).
        var jquery = login.IndexOf("\"/lib/jquery/dist/jquery.min.", StringComparison.Ordinal);
        var validate = login.IndexOf("\"/lib/jquery-validation/dist/jquery.validate.min.", StringComparison.Ordinal);
        var unobtrusive = login.IndexOf("\"/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.", StringComparison.Ordinal);
        Assert.DoesNotContain("/Identity/lib/", login);

        Assert.True(jquery > 0 && jquery < validate && validate < unobtrusive, "jQuery, then validate, then unobtrusive");

        var client = await app.SignedInAsync("assets@example.com");
        Assert.DoesNotContain("jquery", await client.GetStringAsync("/Transactions"));
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/")]
    public async Task EveryLocalScriptAndStylesheetExists(string page)
    {
        var client = app.Browser();
        var html = await client.GetStringAsync(page);
        var assets = System.Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"(/(?:lib|css|js|fonts)/[^\"?]+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(assets);
        foreach (var asset in assets)
            Assert.True((await client.GetAsync(asset)).StatusCode == HttpStatusCode.OK, asset);
    }

    [Fact]
    public async Task TheMonthLineShowsWhatIsLeft()
    {
        var client = await app.SignedInAsync("summary@example.com");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Transactions"));

        Assert.Contains("Kalan", page);
        Assert.Contains("₺0,00", page);
    }
}
