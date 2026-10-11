namespace BudgetApp.Models;

/// <summary>
/// Presentation only: the icon (a Bootstrap Icons name without the "bi-" prefix) and the colours of a category.
/// Every page and the analysis chart read from this one table so a category looks the same everywhere.
/// <para>
/// <see cref="Color"/> is used in the light theme, <see cref="DarkColor"/> is the lighter tone of the same hue
/// for the dark theme (several of the light colours are too dim on a dark card).
/// </para>
/// </summary>
public record CategoryStyle(string Icon, string Color, string DarkColor)
{
    private static readonly Dictionary<string, CategoryStyle> Styles = new()
    {
        // Income
        ["KYK Bursu"] = new("mortarboard-fill", "#0B7A5C", "#34C79A"),
        ["Aile Desteği"] = new("house-heart-fill", "#2F9E78", "#8FD9A8"),
        ["Part-time İş"] = new("briefcase-fill", "#1F8A9E", "#4FC3D9"),
        ["Diğer Gelir"] = new("plus-circle-fill", "#5FAE8E", "#A6D8C3"),

        // Expenses
        ["Market"] = new("basket3-fill", "#E8960C", "#F5B041"),
        ["Kira/Yurt"] = new("house-door-fill", "#5B5BD6", "#8E8EF5"),
        ["Faturalar"] = new("receipt", "#0F8FCB", "#4DB8F0"),
        ["Ulaşım"] = new("bus-front-fill", "#13A89E", "#3FD0C4"),
        ["Eğitim/Kırtasiye"] = new("book-fill", "#8250DF", "#B08CF5"),
        ["Sağlık"] = new("heart-pulse-fill", "#D6409F", "#F06FC0"),
        ["Giyim"] = new("bag-fill", "#C94BD1", "#E07BE6"),
        ["Abonelikler"] = new("collection-play-fill", "#5E6C84", "#9AA8C2"),
        ["Diğer Gider"] = new("three-dots", "#8A97A8", "#B4BFCC"),
        ["Dışarıda Yemek"] = new("egg-fried", "#E4572E", "#F5805C"),
        ["Kafe/Kahve"] = new("cup-hot-fill", "#9C5A1B", "#D69A5B"),
        ["Eğlence"] = new("controller", "#E0457B", "#F5709C")
    };

    private static readonly CategoryStyle Fallback = new("tag-fill", "#8A97A8", "#B4BFCC");

    /// <summary>Every category that has a dedicated style.</summary>
    public static IReadOnlyDictionary<string, CategoryStyle> All => Styles;

    public static CategoryStyle For(string? categoryName) =>
        categoryName is not null && Styles.TryGetValue(categoryName, out var style) ? style : Fallback;

    /// <summary>The inline CSS variables the stylesheet picks the theme's tone from.</summary>
    public string CssVariables => $"--cat: {Color}; --cat-dark: {DarkColor}";
}
