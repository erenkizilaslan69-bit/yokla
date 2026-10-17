using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Models;

/// <summary>The "Söyle ya da yaz" page: a microphone button and a text box.</summary>
public class QuickEntryViewModel
{
    public required QuickEntryQuota Quota { get; init; }

    /// <summary>Why the last attempt failed, as a sentence for the user.</summary>
    public string? Error { get; init; }

    /// <summary>What the user typed, kept when something went wrong.</summary>
    public string? Text { get; init; }

    public int MaxRecordingSeconds { get; init; }

    public int MaxAudioBytes { get; init; }

    public int MaxTextLength { get; init; }
}

/// <summary>
/// The confirmation screen, also used by "Düzenle". It is posted back as a whole: nothing is stored before saving
/// (except, when editing, the transactions being edited).
/// </summary>
public class QuickEntryReviewForm
{
    /// <summary>The sentence as heard or typed; empty when editing saved lines.</summary>
    public string? Heard { get; set; }

    public bool IsDemo { get; set; }

    /// <summary>When editing: the saved quick entry transactions these lines replace.</summary>
    public List<int> EditIds { get; set; } = [];

    public bool IsEdit => EditIds.Count > 0;

    public List<QuickEntryReviewRow> Lines { get; set; } = [];

    /// <summary>Expense and income categories in two groups; the chosen category also decides the type.</summary>
    [ValidateNever]
    public List<SelectListItem> Categories { get; set; } = [];

    public static QuickEntryReviewForm From(QuickEntryProposal proposal) => new()
    {
        Heard = proposal.Heard,
        IsDemo = proposal.IsDemo,
        Lines = proposal.Lines.Select(l => new QuickEntryReviewRow
        {
            Name = l.Description, Amount = l.Amount, CategoryId = l.CategoryId, Date = l.Date, Problem = l.Problem
        }).ToList()
    };

    public static QuickEntryReviewForm ForEdit(IReadOnlyList<Transaction> saved) => new()
    {
        EditIds = saved.Select(t => t.Id).ToList(),
        Lines = saved.Select(t => new QuickEntryReviewRow
        {
            Name = t.ItemName, Amount = t.Amount, CategoryId = t.CategoryId, Date = t.Date
        }).ToList()
    };
}

/// <summary>One line on the confirmation screen; the user can change everything or leave it out.</summary>
public class QuickEntryReviewRow
{
    /// <summary>Unticked lines are not saved.</summary>
    public bool Include { get; set; } = true;

    [Display(Name = "Açıklama")]
    [StringLength(200)]
    public string? Name { get; set; }

    [Display(Name = "Tutar")]
    public decimal? Amount { get; set; }

    [Display(Name = "Kategori")]
    public int CategoryId { get; set; }

    [Display(Name = "Tarih")]
    [DataType(DataType.Date)]
    public DateTime? Date { get; set; }

    /// <summary>Why the line needs a look (from the reader or the rules), shown under it.</summary>
    public string? Problem { get; set; }
}

/// <summary>
/// The message after quick entry saved lines ("3 işlem eklendi: … · Geri al · Düzenle"), carried in TempData to the
/// next page.
/// </summary>
public sealed record QuickEntryFlash(List<int> Ids, string Summary, string? Heard)
{
    public const string Key = "QuickEntrySaved";

    public string ToJson() => JsonSerializer.Serialize(this);

    public static QuickEntryFlash? From(object? value)
    {
        if (value is not string json)
            return null;
        try
        {
            return JsonSerializer.Deserialize<QuickEntryFlash>(json) is { Ids.Count: > 0 } flash ? flash : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
