using System.ComponentModel.DataAnnotations;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Models;

/// <summary>The upload page.</summary>
public class ReceiptScanViewModel
{
    public required ReceiptQuota Quota { get; init; }

    /// <summary>Why the last attempt failed, as a sentence for the user.</summary>
    public string? Error { get; init; }

    public int MaxImageEdge { get; init; }

    public int MaxUploadBytes { get; init; }
}

/// <summary>The confirmation screen. It is posted back as a whole, because nothing of the receipt is stored before saving.</summary>
public class ReceiptReviewForm
{
    [Display(Name = "Fiş tarihi")]
    [Required(ErrorMessage = "Fiş tarihini gir.")]
    [NotInFuture]
    [DataType(DataType.Date)]
    public DateTime? Date { get; set; }

    [Display(Name = "İşletme")]
    [StringLength(100)]
    public string? StoreName { get; set; }

    public decimal? ReceiptTotal { get; set; }

    public decimal GeneralDiscount { get; set; }

    public bool IsDemo { get; set; }

    public List<ReceiptReviewRow> Lines { get; set; } = [];

    public List<string> Notes { get; set; } = [];

    public string? DuplicateWarning { get; set; }

    [ValidateNever]
    public List<SelectListItem> Categories { get; set; } = [];

    public static ReceiptReviewForm From(ReceiptReview review) => new()
    {
        Date = review.Date,
        StoreName = review.StoreName,
        ReceiptTotal = review.ReceiptTotal,
        GeneralDiscount = review.GeneralDiscount,
        IsDemo = review.IsDemo,
        Notes = review.Notes.ToList(),
        DuplicateWarning = review.DuplicateWarning,
        Lines = review.Lines.Select(l => new ReceiptReviewRow
        {
            Include = true,
            Name = l.Name,
            Amount = l.Amount,
            CategoryId = l.CategoryId,
            RawText = l.RawText,
            Quantity = l.Quantity is { } q ? $"{q.ToString("0.###", Tr.Culture)} {l.Unit ?? ""}".Trim() : null
        }).ToList()
    };
}

/// <summary>One line on the confirmation screen; the user can change the name, the amount and the category, or leave it out.</summary>
public class ReceiptReviewRow
{
    /// <summary>Unticked lines are not saved.</summary>
    public bool Include { get; set; } = true;

    [Display(Name = "Ürün adı")]
    [StringLength(200)]
    public string? Name { get; set; }

    [Display(Name = "Tutar")]
    public decimal? Amount { get; set; }

    [Display(Name = "Kategori")]
    public int CategoryId { get; set; }

    /// <summary>The line as printed, shown for comparison.</summary>
    public string? RawText { get; set; }

    /// <summary>"2 adet", "0,845 kg": shown for comparison, not saved.</summary>
    public string? Quantity { get; set; }
}
