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
builder.Services.AddControllersWithViews(options =>
{
    TurkishValidationMessages.Apply(options.ModelBindingMessageProvider);
    // Generates due recurring transactions before every action (see RecurringGenerationFilter).
    options.Filters.AddService<RecurringGenerationFilter>();
});
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

// Receipt reading. With an API key (user-secrets "Gemini:ApiKey" or env "Gemini__ApiKey") the receipts go to Gemini;
// without one Development gets a demo reader with ready-made results, and anywhere else the feature is switched off
// (no reader registered: ReceiptService.IsEnabled is false and the entry points are hidden).
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.Section));
builder.Services.Configure<ReceiptOptions>(builder.Configuration.GetSection(ReceiptOptions.Section));
if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{GeminiOptions.Section}:ApiKey"]))
    builder.Services.AddHttpClient<IReceiptParser, GeminiReceiptParser>();
else if (builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IReceiptParser, DemoReceiptParser>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();

var app = builder.Build();

// Development only: create/upgrade the SQLite file and add the demo account, so `dotnet run` works out of the box.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await DemoDataSeeder.SeedAsync(scope.ServiceProvider, DateTime.Today);
}

var trCulture = new CultureInfo("tr-TR");
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
