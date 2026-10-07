using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

public class Category
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public TransactionType Type { get; set; }

    public bool IsUnnecessary { get; set; }

    /// <summary>Fixed expense (rent, bills, subscriptions): counted as-is in the month-end forecast.</summary>
    public bool IsFixed { get; set; }
}
