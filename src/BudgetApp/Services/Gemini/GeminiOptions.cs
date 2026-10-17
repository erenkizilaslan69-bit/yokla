namespace BudgetApp.Services;

/// <summary>"Gemini" section of the configuration. The key comes from user-secrets ("Gemini:ApiKey") or the
/// environment ("Gemini__ApiKey"), never from appsettings.json.</summary>
public sealed class GeminiOptions
{
    public const string Section = "Gemini";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-3.8-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>How long one receipt may take before the user is told to try again.</summary>
    public double TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// "generationConfig.thinkingConfig.thinkingLevel": how much the model thinks before answering. Reading a receipt
    /// needs little, and thinking is what makes an answer slow, so the default is the lowest level gemini-3.8-flash
    /// accepts ("low"; it rejects "minimal", other models accept it). "medium" and "high" are the other values.
    /// Empty leaves the field out and the model uses its own default (medium for gemini-3.8-flash).
    /// </summary>
    public string? ThinkingLevel { get; set; } = "low";
}
