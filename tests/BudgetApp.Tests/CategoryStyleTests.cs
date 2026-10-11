using System.Text.RegularExpressions;
using BudgetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Tests;

public class CategoryStyleTests
{
    [Fact]
    public async Task EverySeededCategoryHasADedicatedStyle()
    {
        using var t = new TestDb();

        var names = await t.Context.Categories.Select(c => c.Name).ToListAsync();

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.True(CategoryStyle.All.ContainsKey(name), $"'{name}' has no CategoryStyle"));
    }

    [Fact]
    public async Task ThereIsNoStyleForACategoryThatDoesNotExist()
    {
        using var t = new TestDb();
        var names = (await t.Context.Categories.Select(c => c.Name).ToListAsync()).ToHashSet();

        Assert.All(CategoryStyle.All.Keys, key => Assert.Contains(key, names));
    }

    [Fact]
    public void Styles_HaveHexColoursAndIconNames()
    {
        Assert.All(CategoryStyle.All.Values, style =>
        {
            Assert.Matches(new Regex("^#[0-9A-Fa-f]{6}$"), style.Color);
            Assert.Matches(new Regex("^[a-z0-9-]+$"), style.Icon);
            Assert.False(style.Icon.StartsWith("bi-"), "store the icon name without the bi- prefix");
        });
    }

    [Fact]
    public void Colours_AreUniquePerCategorySoChartSlicesAreDistinguishable()
    {
        var colours = CategoryStyle.All.Values.Select(s => s.Color.ToUpperInvariant()).ToList();

        Assert.Equal(colours.Count, colours.Distinct().Count());
    }

    [Fact]
    public void DarkColours_AreHexColoursForEveryCategoryAndTheFallback()
    {
        var hex = new Regex("^#[0-9A-Fa-f]{6}$");

        Assert.All(CategoryStyle.All.Values, style => Assert.Matches(hex, style.DarkColor));
        Assert.Matches(hex, CategoryStyle.For("Olmayan Kategori").DarkColor);
    }

    [Fact]
    public void DarkColours_AreUniquePerCategorySoChartSlicesStayDistinguishableInTheDarkTheme()
    {
        var colours = CategoryStyle.All.Values.Select(s => s.DarkColor.ToUpperInvariant()).ToList();

        Assert.Equal(colours.Count, colours.Distinct().Count());
    }

    [Fact]
    public void For_ReturnsTheSameStyleEveryTime()
    {
        Assert.Equal(CategoryStyle.For("Market"), CategoryStyle.For("Market"));
        Assert.Equal("#E8960C", CategoryStyle.For("Market").Color);
        Assert.Equal("basket3-fill", CategoryStyle.For("Market").Icon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Olmayan Kategori")]
    public void For_FallsBackToANeutralStyleForUnknownCategories(string? name)
    {
        var style = CategoryStyle.For(name);

        Assert.Equal("tag-fill", style.Icon);
        Assert.False(CategoryStyle.All.ContainsKey(name ?? string.Empty));
    }
}
