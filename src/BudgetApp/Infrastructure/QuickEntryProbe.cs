using System.Diagnostics;
using BudgetApp.Models;
using BudgetApp.Services;
using Microsoft.Extensions.Options;

namespace BudgetApp.Infrastructure;

/// <summary>
/// A one-off check for Development: sends one recording (e.g. an iPhone Voice Memos .m4a) to the real quick entry
/// reader, the same one the app uses, and prints what came back and what the app would do with it. Nothing is saved
/// and the attempt does not count towards the daily limits. Needs the API key in user-secrets.
/// <code>
/// dotnet run --project src/BudgetApp -- quick-entry-test kayit.m4a
/// dotnet run --project src/BudgetApp -- quick-entry-test kayit.m4a --mime audio/mp4
/// </code>
/// "--mime" sends the recording with another type than the one detected from its bytes, to see what Gemini accepts.
/// </summary>
public static class QuickEntryProbe
{
    public const string Command = "quick-entry-test";

    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, string[] args)
    {
        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine($"{Command} only runs in Development.");
            return 2;
        }
        if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--mime"))
        {
            Console.Error.WriteLine($"Usage: dotnet run --project src/BudgetApp -- {Command} <recording> [--mime <type>]");
            return 2;
        }

        var path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 2;
        }

        using var scope = services.CreateScope();
        var parser = scope.ServiceProvider.GetService<IQuickEntryParser>();
        if (parser is null or { IsDemo: true })
        {
            Console.Error.WriteLine("No Gemini key: set it with");
            Console.Error.WriteLine("  dotnet user-secrets set \"Gemini:ApiKey\" \"<key>\" --project src/BudgetApp");
            return 2;
        }

        var data = await File.ReadAllBytesAsync(path);
        var detected = QuickEntryAudio.DetectMimeType(data);
        var mimeType = args.Length == 3 ? args[2] : detected;
        var limits = scope.ServiceProvider.GetRequiredService<IOptions<QuickEntryOptions>>().Value;

        Console.WriteLine($"File:      {path}");
        Console.WriteLine($"Size:      {data.Length / 1024} KB{(data.Length > limits.MaxAudioBytes ? $"  (the app refuses more than {limits.MaxAudioBytes / 1024} KB)" : "")}");
        Console.WriteLine($"Detected:  {detected ?? "not a recording the app accepts"}");
        if (mimeType is null)
            return 1;
        Console.WriteLine($"Sent as:   {mimeType}");
        Console.WriteLine();

        var all = await scope.ServiceProvider.GetRequiredService<ICategoryService>().GetAllAsync();
        var today = scope.ServiceProvider.GetRequiredService<AppClock>().Today;
        var context = new QuickEntryContext(today,
            all.Where(c => c.Type == TransactionType.Expense).Select(c => c.Name).ToList(),
            all.Where(c => c.Type == TransactionType.Income).Select(c => c.Name).ToList());

        var watch = Stopwatch.StartNew();
        QuickEntryParseResult result;
        try
        {
            result = await parser.ParseAsync(QuickEntryInput.FromAudio(new QuickEntryAudio(data, mimeType)), context, CancellationToken.None);
        }
        catch (QuickEntryException e)
        {
            Console.WriteLine($"FAILED after {watch.ElapsedMilliseconds} ms: {e.Kind} ({e.Detail})");
            Console.WriteLine($"The user would see: {e.Message}");
            return 1;
        }

        Console.WriteLine($"Answer in {watch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Heard:     \"{result.Heard}\"");
        Console.WriteLine($"Is entry:  {result.IsEntry}");
        foreach (var item in result.Items)
            Console.WriteLine($"  - {item.Description} | {item.Amount?.ToString() ?? "no amount"} | {item.Type} | {item.Category} | " +
                              $"{item.Date?.ToString("yyyy-MM-dd") ?? "today"} | {item.Confidence}{(item.Issue is null ? "" : " | " + item.Issue)}");

        var lines = QuickEntryRules.Resolve(result.Items, all, today);
        Console.WriteLine();
        Console.WriteLine(result.IsEntry && lines.Count > 0
            ? QuickEntryRules.CanAutoSave(result.Heard, result.Items, lines, today)
                ? "The app would save these lines right away."
                : "The app would open the confirmation screen."
            : "The app would say it heard no transactions.");
        foreach (var line in lines.Where(l => l.Problem is not null))
            Console.WriteLine($"  {line.Description}: {line.Problem}");
        return 0;
    }
}
