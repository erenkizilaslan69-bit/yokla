using System.Net;
using BudgetApp.Models;

namespace BudgetApp.Tests.Web;

/// <summary>Sign-in, sign-up and sign-out are the only Identity pages; everything else from the package is closed.</summary>
public class IdentityPagesTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("/Identity/Account/ForgotPassword")]
    [InlineData("/Identity/Account/ResetPassword")]
    [InlineData("/Identity/Account/ResendEmailConfirmation")]
    [InlineData("/Identity/Account/ConfirmEmail")]
    [InlineData("/Identity/Account/Lockout")]
    [InlineData("/Identity/Account/AccessDenied")]
    [InlineData("/Identity/Account/ExternalLogin")]
    [InlineData("/Identity/Account/LoginWith2fa")]
    [InlineData("/Identity/Account/Manage")]
    [InlineData("/Identity/Account/Manage/Email")]
    [InlineData("/Identity/Account/Manage/ChangePassword")]
    [InlineData("/Identity/Account/Manage/PersonalData")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData")]
    [InlineData("/Identity/Account/Manage/TwoFactorAuthentication")]
    public async Task PackagePagesAreClosedEvenWhenSignedIn(string url)
    {
        var anonymous = app.Browser();
        var signedIn = await app.SignedInAsync($"closed{Math.Abs(url.GetHashCode())}@example.com");

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await signedIn.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task DeletingPersonalDataCannotBePosted()
    {
        var client = await app.SignedInAsync("delete@example.com");

        var response = await client.PostAsync("/Identity/Account/Manage/DeletePersonalData",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Password"] = "Gizli123!" }));

        // No page answers (an unknown address that is posted to gets 404 or 405); the account is still there.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Transactions")).StatusCode);
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    public async Task OwnPagesAreOpen(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await app.Browser().GetAsync(url)).StatusCode);

    [Fact]
    public async Task SigningOutStillWorks()
    {
        var client = await app.SignedInAsync("logout@example.com");

        var response = await client.PostFormAsync("/Identity/Account/Logout", "/", new());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", (await client.GetAsync("/Transactions")).Headers.Location!.ToString());
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccount()
    {
        var client = await app.SignedInAsync("lock@example.com");
        await client.PostFormAsync("/Identity/Account/Logout", "/", new());

        Task<HttpResponseMessage> SignIn(string password) => client.PostFormAsync("/Identity/Account/Login", "/Identity/Account/Login",
            new() { ["Input.Email"] = "lock@example.com", ["Input.Password"] = password });

        async Task<string> Text(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

        for (var i = 0; i < 4; i++)
            Assert.Contains("E-posta veya şifre hatalı.", await Text(await SignIn("yanlis")));
        Assert.Contains("Çok fazla hatalı deneme yapıldı", await Text(await SignIn("yanlis"))); // the fifth locks

        var locked = await SignIn("Gizli123!");
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode); // the form again, not a redirect home
        Assert.Contains("Çok fazla hatalı deneme yapıldı", await Text(locked));
    }

    [Theory]
    [InlineData("/Transactions?year=99999&month=1")]
    [InlineData("/Analysis?year=99999&month=1")]
    [InlineData("/Transactions?year=0&month=13")]
    public async Task AnImpossibleMonthInTheAddressShowsTheCurrentMonth(string url)
    {
        var client = await app.SignedInAsync($"month{Math.Abs(url.GetHashCode())}@example.com");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }
}

public class MonthPickerTests
{
    private static readonly DateTime Today = new(2026, 10, 15);

    [Theory]
    [InlineData(2026, 9, 2026, 9)]
    [InlineData(2026, 10, 2026, 10)]
    [InlineData(2026, 11, 2026, 10)]   // future
    [InlineData(1999, 5, 2026, 10)]    // too early
    [InlineData(99999, 1, 2026, 10)]   // beyond DateTime
    [InlineData(10000, 1, 2026, 10)]
    [InlineData(2026, 0, 2026, 10)]
    [InlineData(2026, 13, 2026, 10)]
    public void Resolve_FallsBackToTheCurrentMonthForInvalidValues(int year, int month, int expectedYear, int expectedMonth) =>
        Assert.Equal((expectedYear, expectedMonth), MonthPickerViewModel.Resolve(year, month, Today));

    [Fact]
    public void Resolve_WithoutValuesIsTheCurrentMonth() =>
        Assert.Equal((2026, 10), MonthPickerViewModel.Resolve(null, null, Today));
}
