using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

/// <summary>Rejects dates later than today (server-side form validation).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotInFutureAttribute : ValidationAttribute
{
    public NotInFutureAttribute() : base("Tarih bugünden ileri olamaz.") { }

    public override bool IsValid(object? value) =>
        value is not DateTime date || date.Date <= DateTime.Today;
}
