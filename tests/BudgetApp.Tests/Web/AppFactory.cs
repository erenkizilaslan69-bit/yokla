using System.Net;
using System.Text.RegularExpressions;
using BudgetApp.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BudgetApp.Tests.Web;

/// <summary>
/// The whole app in memory, over HTTP: the "Testing" environment (no demo account, no Gemini key, so receipt reading
/// and quick entry are off) and an in-memory SQLite database with the real schema. No network, no files.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Gemini:ApiKey", "");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            _connection.Open();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();
        return host;
    }

    /// <summary>A browser-like client: keeps cookies, does not follow redirects (so tests can see them).</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>A new account, signed in on the returned client.</summary>
    public async Task<HttpClient> SignedInAsync(string email = "ayse@example.com", string password = "Gizli123!")
    {
        var client = Browser();
        var response = await client.PostFormAsync("/Identity/Account/Register", "/Identity/Account/Register", new()
        {
            ["Input.Email"] = email, ["Input.Password"] = password, ["Input.ConfirmPassword"] = password
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

public static partial class HttpClientForms
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    /// <summary>The antiforgery token of a page, as a browser would send it back with the form.</summary>
    public static async Task<string> TokenAsync(this HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        return TokenPattern().Match(html) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException($"No antiforgery token on {page}.");
    }

    /// <summary>Posts a form the way the page at <paramref name="formPage"/> would, token included.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, string formPage, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await client.TokenAsync(formPage);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }
}
