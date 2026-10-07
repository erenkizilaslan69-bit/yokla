using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace BudgetApp.Localization;

/// <summary>Turkish replacements for MVC's built-in model binding messages (e.g. "The value 'abc' is not valid.").</summary>
public static class TurkishValidationMessages
{
    public static void Apply(DefaultModelBindingMessageProvider provider)
    {
        provider.SetValueMustNotBeNullAccessor(_ => "Bu alan zorunlu.");
        provider.SetMissingBindRequiredValueAccessor(name => $"'{name}' alanı zorunlu.");
        provider.SetMissingKeyOrValueAccessor(() => "Bir değer gerekli.");
        provider.SetMissingRequestBodyRequiredValueAccessor(() => "İstek gövdesi boş olamaz.");
        provider.SetAttemptedValueIsInvalidAccessor((value, name) => $"'{value}' değeri '{name}' alanı için geçersiz.");
        provider.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"'{value}' değeri geçersiz.");
        provider.SetUnknownValueIsInvalidAccessor(name => $"'{name}' alanı için girilen değer geçersiz.");
        provider.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Girilen değer geçersiz.");
        provider.SetValueIsInvalidAccessor(value => $"'{value}' değeri geçersiz.");
        provider.SetValueMustBeANumberAccessor(name => $"'{name}' alanı bir sayı olmalı.");
        provider.SetNonPropertyValueMustBeANumberAccessor(() => "Bir sayı girmelisin.");
    }
}
