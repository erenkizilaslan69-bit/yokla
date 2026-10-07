namespace BudgetApp.Models;

/// <summary>
/// Presentation only: the icon (a Bootstrap Icons name without the "bi-" prefix) and the colour of a category.
/// Every page and the analysis chart read from this one table so a category looks the same everywhere.
/// </summary>
public record CategoryStyle(string Icon, string Color)
{
    private static readonly Dictionary<string, CategoryStyle> Styles = new()
    {
        // Income
        ["KYK Bursu"] = new("mortarboard-fill", "#0B7A5C"),
        ["Aile Desteği"] = new("house-heart-fill", "#2F9E78"),
        ["Part-time İş"] = new("briefcase-fill", "#1F8A9E"),
        ["Diğer Gelir"] = new("plus-circle-fill", "#5FAE8E"),

        // Expenses
        ["Market"] = new("basket3-fill", "#E8960C"),
        ["Kira/Yurt"] = new("house-door-fill", "#5B5BD6"),
        ["Faturalar"] = new("receipt", "#0F8FCB"),
        ["Ulaşım"] = new("bus-front-fill", "#13A89E"),
        ["Eğitim/Kırtasiye"] = new("book-fill", "#8250DF"),
        ["Sağlık"] = new("heart-pulse-fill", "#D6409F"),
        ["Giyim"] = new("bag-fill", "#C94BD1"),
        ["Abonelikler"] = new("collection-play-fill", "#5E6C84"),
        ["Diğer Gider"] = new("three-dots", "#8A97A8"),
        ["Dışarıda Yemek"] = new("egg-fried", "#E4572E"),
        ["Kafe/Kahve"] = new("cup-hot-fill", "#9C5A1B"),
        ["Eğlence"] = new("controller", "#E0457B")
    };

    private static readonly CategoryStyle Fallback = new("tag-fill", "#8A97A8");

    /// <summary>Every category that has a dedicated style.</summary>
    public static IReadOnlyDictionary<string, CategoryStyle> All => Styles;

    public static CategoryStyle For(string? categoryName) =>
        categoryName is not null && Styles.TryGetValue(categoryName, out var style) ? style : Fallback;
}
