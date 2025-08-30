namespace AiTranslatorDotnet.Contracts;

/// <summary>
/// Response DTO for translation.
/// </summary>
public sealed class TranslateResponse
{
    /// <summary>
    /// Echoes the requested source language or "auto" when auto-detected.
    /// </summary>
    public string SourceLang { get; set; } = "auto";

    /// <summary>
    /// The target language used for translation.
    /// </summary>
    public string TargetLang { get; set; } = string.Empty;

    /// <summary>
    /// The translated text returned by the model.
    /// </summary>
    public string TranslatedText { get; set; } = string.Empty;
}