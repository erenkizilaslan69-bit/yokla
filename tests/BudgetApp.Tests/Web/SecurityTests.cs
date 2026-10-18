using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BudgetApp.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApp.Tests.Web;

/// <summary>Every controller action is signed-in only (except the public home pages) and every POST checks the
/// antiforgery token. Checked on the code, so a new controller or action cannot forget it.</summary>
public class ControllerRulesTests
{
    private static readonly Type[] Controllers = typeof(HomeController).Assembly.GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
        .ToArray();

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

    [Fact]
    public void EveryControllerButHomeNeedsASignedInUser()
    {
        Assert.NotEmpty(Controllers);
        var open = Controllers.Where(c => c.GetCustomAttribute<AuthorizeAttribute>() is null).Select(c => c.Name);

        Assert.Equal(["HomeController"], open);
        Assert.All(Controllers.SelectMany(Actions), a => Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    [Fact]
    public void EveryPostChecksTheAntiforgeryToken()
    {
        var posts = Controllers.SelectMany(Actions).Where(a => a.GetCustomAttribute<HttpPostAttribute>() is not null).ToList();

        Assert.NotEmpty(posts);
        Assert.All(posts, a => Assert.True(a.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() is not null,
            $"{a.DeclaringType!.Name}.{a.Name} is a POST without [ValidateAntiForgeryToken]"));
    }
}

/// <summary>The same rules over HTTP, against the whole app.</summary>
public partial class SecurityTests(AppFactory app) : IClassFixture<AppFactory>
{
    [GeneratedRegex(@"/Transactions/Edit/(\d+)")]
    private static partial Regex EditLink();

    private static async Task<int> AddExpenseAsync(HttpClient client, string item)
    {
        var response = await client.PostFormAsync("/Transactions/Create", "/Transactions/Create", new()
        {
            ["Type"] = "Expense", ["Amount"] = "42,50", ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["CategoryId"] = "5", ["ItemName"] = item
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await client.GetStringAsync("/Transactions");
        return int.Parse(EditLink().Match(list).Groups[1].Value);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Home/Privacy")]
    public async Task PublicPagesOpenWithoutSigningIn(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await app.Browser().GetAsync(url)).StatusCode);

    [Theory]
    [InlineData("/Transactions")]
    [InlineData("/Transactions/ExportCsv")]
    [InlineData("/Analysis")]
    [InlineData("/Goals")]
    [InlineData("/Budgets")]
    [InlineData("/Recurring")]
    public async Task PrivatePagesSendAStrangerToSignIn(string url)
    {
        var response = await app.Browser().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task APostWithoutTheTokenIsRefused()
    {
        var client = await app.SignedInAsync("csrf@example.com");

        var response = await client.PostAsync("/Transactions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Type"] = "Expense", ["Amount"] = "10", ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"), ["CategoryId"] = "5"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("/Transactions/Edit/", await client.GetStringAsync("/Transactions"));
    }

    [Fact]
    public async Task AnotherUsersTransactionCannotBeSeenChangedOrDeleted()
    {
        var alice = await app.SignedInAsync("alice.web@example.com");
        var bob = await app.SignedInAsync("bob.web@example.com");
        var id = await AddExpenseAsync(alice, "Alicenin kitabı");

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/Transactions/Edit/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/Transactions/Delete/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostFormAsync($"/Transactions/Delete/{id}", "/Transactions/Create", new())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostFormAsync($"/Transactions/Edit/{id}", "/Transactions/Create", new()
        {
            ["Type"] = "Expense", ["Amount"] = "1", ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"), ["CategoryId"] = "5"
        })).StatusCode);

        // Pages are HTML-encoded ("ı" arrives as an entity): compare the decoded text, or a missing name proves nothing.
        static async Task<string> Text(HttpClient c, string url) => WebUtility.HtmlDecode(await c.GetStringAsync(url));
        Assert.DoesNotContain("Alicenin kitabı", await Text(bob, "/Transactions"));
        Assert.DoesNotContain("Alicenin kitabı", await Text(bob, "/Transactions/ExportCsv"));
        Assert.Contains("Alicenin kitabı", await Text(alice, "/Transactions"));
        Assert.Contains("Alicenin kitabı", await Text(alice, "/Transactions/ExportCsv"));
        Assert.Contains("42,50", await alice.GetStringAsync($"/Transactions/Edit/{id}"));
    }

    [Fact]
    public async Task ReceiptsAndQuickEntryAreOffWithoutAKeyOutsideDevelopment()
    {
        var client = await app.SignedInAsync("nokey@example.com");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Receipts/Scan")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/QuickEntry")).StatusCode);
        Assert.DoesNotContain("Söyle ya da yaz", WebUtility.HtmlDecode(await client.GetStringAsync("/Transactions")));
    }
}
