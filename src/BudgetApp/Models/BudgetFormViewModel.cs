using System.ComponentModel.DataAnnotations;
using BudgetApp.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BudgetApp.Models;

public class BudgetFormViewModel
{
    [Display(Name = "Kategori")]
    [Range(1, int.MaxValue, ErrorMessage = "Kategori seçin.")]
    public int CategoryId { get; set; }

    [Display(Name = "Aylık limit (TL)")]
    [Required(ErrorMessage = "Limit zorunlu.")]
    [Range(typeof(decimal), "0,01", "999999999", ErrorMessage = "Limit 0'dan büyük olmalı.")]
    public decimal? MonthlyLimit { get; set; }

    public List<SelectListItem> Categories { get; set; } = [];
}

public class BudgetsIndexViewModel
{
    public required List<BudgetProgress> Items { get; init; }
    public required BudgetFormViewModel Form { get; init; }

    /// <summary>The month the spending is measured in (always the current one).</summary>
    public required DateTime Month { get; init; }
}
