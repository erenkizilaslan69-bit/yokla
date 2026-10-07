using BudgetApp.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace BudgetApp.Tests;

public class LocalizationTests
{
    private readonly TurkishIdentityErrorDescriber _describer = new();

    [Fact]
    public void IdentityErrors_AreTurkishAndKeepTheStandardCodes()
    {
        var english = new IdentityErrorDescriber();

        Assert.Equal(english.PasswordRequiresDigit().Code, _describer.PasswordRequiresDigit().Code);
        Assert.Equal(english.PasswordRequiresUpper().Code, _describer.PasswordRequiresUpper().Code);
        Assert.Equal(english.DuplicateEmail("a@b.c").Code, _describer.DuplicateEmail("a@b.c").Code);
        Assert.Contains("rakam", _describer.PasswordRequiresDigit().Description);
        Assert.Contains("büyük harf", _describer.PasswordRequiresUpper().Description);
        Assert.Contains("küçük harf", _describer.PasswordRequiresLower().Description);
    }

    [Fact]
    public void IdentityErrors_IncludeTheOffendingValues()
    {
        Assert.Contains("6", _describer.PasswordTooShort(6).Description);
        Assert.Contains("a@b.c", _describer.DuplicateEmail("a@b.c").Description);
        Assert.Contains("x", _describer.InvalidEmail("x").Description);
        Assert.Contains("a@b.c", _describer.DuplicateUserName("a@b.c").Description);
    }

    [Fact]
    public void IdentityErrors_HaveNoEnglishLeftovers()
    {
        var all = new[]
        {
            _describer.DefaultError(), _describer.ConcurrencyFailure(), _describer.PasswordMismatch(),
            _describer.InvalidToken(), _describer.LoginAlreadyAssociated(), _describer.InvalidUserName("u"),
            _describer.InvalidEmail("e"), _describer.DuplicateUserName("u"), _describer.DuplicateEmail("e"),
            _describer.InvalidRoleName("r"), _describer.DuplicateRoleName("r"), _describer.UserAlreadyHasPassword(),
            _describer.UserLockoutNotEnabled(), _describer.UserAlreadyInRole("r"), _describer.UserNotInRole("r"),
            _describer.PasswordTooShort(6), _describer.PasswordRequiresNonAlphanumeric(), _describer.PasswordRequiresDigit(),
            _describer.PasswordRequiresLower(), _describer.PasswordRequiresUpper(), _describer.PasswordRequiresUniqueChars(2),
            _describer.RecoveryCodeRedemptionFailed()
        };

        Assert.All(all, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Code));
            Assert.DoesNotContain("Passwords must", e.Description);
            Assert.DoesNotContain(" is already ", e.Description);
            Assert.DoesNotContain("User ", e.Description);
        });
    }

    [Fact]
    public void ModelBindingMessages_AreTurkish()
    {
        var provider = new DefaultModelBindingMessageProvider();
        TurkishValidationMessages.Apply(provider);

        Assert.Equal("'Tutar' alanı bir sayı olmalı.", provider.ValueMustBeANumberAccessor("Tutar"));
        Assert.Equal("'abc' değeri 'Tarih' alanı için geçersiz.", provider.AttemptedValueIsInvalidAccessor("abc", "Tarih"));
        Assert.Equal("Bu alan zorunlu.", provider.ValueMustNotBeNullAccessor("x"));
        Assert.Equal("Bir sayı girmelisin.", provider.NonPropertyValueMustBeANumberAccessor());
        Assert.Equal("'x' değeri geçersiz.", provider.ValueIsInvalidAccessor("x"));
    }
}
