using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

public class GoalFormViewModel
{
    [Display(Name = "Hedef adı")]
    [Required(ErrorMessage = "Hedef adı zorunlu.")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Hedef tutar (TL)")]
    [Required(ErrorMessage = "Hedef tutar zorunlu.")]
    [Range(typeof(decimal), "0,01", "999999999", ErrorMessage = "Tutar 0'dan büyük olmalı.")]
    public decimal? TargetAmount { get; set; }
}
