namespace AiTranslatorDotnet.Contracts;

/// <summary>
/// Request DTO for translation.
/// </summary>
public sealed class TranslateRequest
{
    /// <summary>
    /// The input text to translate. Required.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional source language (e.g., "en", "zh-CN").
    /// If null or empty, the service will auto-detect.
    /// </summary>
    public string? SourceLang { get; set; }

    /// <summary>
    /// Target language (e.g., "en", "de", "ja"). Required.
    /// </summary>
    public string TargetLang { get; set; } = string.Empty;
}