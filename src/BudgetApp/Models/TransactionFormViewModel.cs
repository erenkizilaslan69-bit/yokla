using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Models;

public class TransactionFormViewModel
{
    public int Id { get; set; }

    [Display(Name = "Tür")]
    public TransactionType Type { get; set; } = TransactionType.Expense;

    [Display(Name = "Tutar (TL)")]
    [Range(typeof(decimal), "0,01", "999999999", ErrorMessage = "Tutar 0'dan büyük olmalı.")]
    [Required(ErrorMessage = "Tutar zorunlu.")]
    public decimal? Amount { get; set; }

    [Display(Name = "Tarih")]
    [Required(ErrorMessage = "Tarih zorunlu.")]
    [DataType(DataType.Date)]
    [NotInFuture]
    public DateTime? Date { get; set; } = DateTime.Today;

    [Display(Name = "Kategori")]
    [Range(1, int.MaxValue, ErrorMessage = "Kategori seçin.")]
    public int CategoryId { get; set; }

    [Display(Name = "Ürün adı")]
    [StringLength(200)]
    public string? ItemName { get; set; }

    [Display(Name = "Not")]
    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>Only used when adding: also create a monthly recurring definition from this transaction.</summary>
    [Display(Name = "Her ay tekrarla")]
    public bool RepeatMonthly { get; set; }

    public List<SelectListItem> Categories { get; set; } = [];
}
