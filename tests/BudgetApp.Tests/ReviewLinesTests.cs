using BudgetApp.Models;
using BudgetApp.Services;

namespace BudgetApp.Tests;

public class ReviewLinesTests
{
    private static readonly DateTime Today = new(2026, 10, 18);

    private static readonly Category[] Categories =
    [
        new() { Id = 1, Name = "KYK Bursu", Type = TransactionType.Income },
        new() { Id = 5, Name = "Market", Type = TransactionType.Expense }
    ];

    private static ReviewResult Receipt(params ReviewRow[] rows) => ReviewLines.Check(rows, Categories, ReviewLines.Receipt, Today);
    private static ReviewResult Quick(params ReviewRow[] rows) => ReviewLines.Check(rows, Categories, ReviewLines.QuickEntry, Today);

    private static IEnumerable<(int, string)> Where(ReviewResult r) => r.Errors.Select(e => (e.Line, e.Field));

    [Fact]
    public void Check_GoodLinesPassWithTheirCategory()
    {
        var result = Quick(new ReviewRow(true, "Simit", 20m, 5, Today), new ReviewRow(true, "Burs", 3000m, 1, Today.AddDays(-1)));

        Assert.True(result.IsValid);
        Assert.Equal([TransactionType.Expense, TransactionType.Income], result.Chosen.Select(c => c.Category.Type));
    }

    [Fact]
    public void Check_UntickedLinesAreSkippedEvenWhenWrong()
    {
        var result = Receipt(new ReviewRow(true, "Süt", 42.5m, 5), new ReviewRow(false, "", null, 99));

        Assert.True(result.IsValid);
        Assert.Equal("Süt", Assert.Single(result.Chosen).Row.Name);
    }

    [Fact]
    public void Check_NamesEveryProblemOfALine()
    {
        var result = Quick(new ReviewRow(true, " ", 0m, 99, Today.AddDays(1)));

        Assert.Equal([(0, "Name"), (0, "Amount"), (0, "CategoryId"), (0, "Date")], Where(result));
        Assert.Equal(["Açıklama yaz.", "Tutar 0'dan büyük olmalı.", "Bir kategori seç.", "Tarih ileride olamaz."], result.Errors.Select(e => e.Message));
        Assert.Empty(result.Chosen);
    }

    [Fact]
    public void Check_AnAmountThatRoundsToZeroIsNoAmount() =>
        Assert.Equal([(0, "Amount")], Where(Receipt(new ReviewRow(true, "Poşet", 0.004m, 5))));

    [Fact]
    public void Check_AReceiptTakesOnlyExpenseCategoriesAndNeedsNoDatePerLine()
    {
        var result = Receipt(new ReviewRow(true, "Süt", 42.5m, 1));

        Assert.Equal([(0, "CategoryId")], Where(result));
        Assert.Equal("Bir gider kategorisi seç.", result.Errors[0].Message);
    }

    [Fact]
    public void Check_AQuickEntryLineNeedsADate() =>
        Assert.Equal([(0, "Date")], Where(Quick(new ReviewRow(true, "Simit", 20m, 5, null))));

    [Fact]
    public void Check_NothingTickedIsAnErrorOfTheForm()
    {
        var result = Receipt(new ReviewRow(false, "Süt", 42.5m, 5));

        var error = Assert.Single(result.Errors);
        Assert.Equal((-1, "", "Kaydetmek için en az bir satır seç."), (error.Line, error.Field, error.Message));
    }
}
