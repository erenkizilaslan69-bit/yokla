using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using BudgetApp.Data;
using BudgetApp.Infrastructure;
using BudgetApp.Localization;
using BudgetApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// No email sender is configured, so accounts are usable immediately after registration.
builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddErrorDescriber<TurkishIdentityErrorDescriber>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
// The Identity UI package brings many pages (account management, password reset, two-factor, delete personal data);
// only the app's own sign-in, sign-up and sign-out pages are reachable, the rest answer 404 (see IdentityPages).
builder.Services.AddRazorPages(options => IdentityPages.CloseAllExceptOwn(options.Conventions));
builder.Services.AddControllersWithViews(options =>
{
    TurkishValidationMessages.Apply(options.ModelBindingMessageProvider);
    // Generates due recurring transactions before every action (see RecurringGenerationFilter).
    options.Filters.AddService<RecurringGenerationFilter>();
});
// One clock for the whole app; in Development "Clock:Today" can pin the date (see AppClock).
builder.Services.AddSingleton(AppClock.For(builder.Configuration, builder.Environment));
builder.Services.AddSingleton<AppClock>();
builder.Services.AddScoped<RecurringGenerationFilter>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IGoalService, GoalService>();
builder.Services.AddScoped<IMonthCloseService, MonthCloseService>();
builder.Services.AddScoped<IForecastService, ForecastService>();
builder.Services.AddScoped<IWarningService, WarningService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ITransactionCsvExporter, TransactionCsvExporter>();
builder.Services.AddScoped<IDailyAllowanceService, DailyAllowanceService>();

// Receipt reading and quick entry ("Söyle ya da yaz"). With an API key (user-secrets "Gemini:ApiKey" or env
// "Gemini__ApiKey") both go to Gemini; without one Development gets demo readers, and anywhere else both features are
// switched off (no reader registered: IsEnabled is false and the entry points are hidden).
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.Section));
builder.Services.Configure<ReceiptOptions>(builder.Configuration.GetSection(ReceiptOptions.Section));
builder.Services.Configure<QuickEntryOptions>(builder.Configuration.GetSection(QuickEntryOptions.Section));
if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{GeminiOptions.Section}:ApiKey"]))
{
    // HttpClient's own logging may list request headers at Trace level; the API key header must never show up.
    builder.Services.AddHttpClient<IReceiptParser, GeminiReceiptParser>().RedactLoggedHeaders(_ => true);
    builder.Services.AddHttpClient<IQuickEntryParser, GeminiQuickEntryParser>().RedactLoggedHeaders(_ => true);
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IReceiptParser, DemoReceiptParser>();
    builder.Services.AddSingleton<IQuickEntryParser, DemoQuickEntryParser>();
}
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<IQuickEntryService, QuickEntryService>();

var app = builder.Build();

// Development only: create/upgrade the SQLite file and add the demo account, so `dotnet run` works out of the box.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await DemoDataSeeder.SeedAsync(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<AppClock>().Today);
}

// Development only: `dotnet run --project src/BudgetApp -- quick-entry-test <recording>` sends one recording to the
// real quick entry reader, prints the result and exits (see QuickEntryProbe).
if (args.Length > 0 && args[0] == QuickEntryProbe.Command)
{
    Environment.ExitCode = await QuickEntryProbe.RunAsync(app.Services, app.Environment, args[1..]);
    return;
}

// tr-TR, with the real minus sign for negative numbers (see AppCulture).
var trCulture = AppCulture.Turkish;
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(trCulture),
    SupportedCultures = [trCulture],
    SupportedUICultures = [trCulture]
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
