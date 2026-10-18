using System.ComponentModel.DataAnnotations;
using BudgetApp.Infrastructure;

namespace BudgetApp.Models;

/// <summary>Rejects dates later than today (server-side form validation).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotInFutureAttribute : ValidationAttribute
{
    public NotInFutureAttribute() : base("Tarih bugünden ileri olamaz.") { }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var today = (validationContext.GetService(typeof(AppClock)) as AppClock)?.Today ?? DateTime.Today;
        return value is not DateTime date || date.Date <= today
            ? ValidationResult.Success
            : new ValidationResult(ErrorMessage, validationContext.MemberName is { } member ? [member] : null);
    }
}
