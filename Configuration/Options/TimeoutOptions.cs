namespace AiTranslatorDotnet.Configuration.Options;

/// <summary>
/// Centralized timeout configuration for the entire application.
/// Bind this from configuration section "Timeout" (e.g., appsettings.json).
/// </summary>
public sealed class TimeoutOptions
{
    public const string SectionName = "Timeout";

    /// <summary>
    /// HTTP timeout (in seconds) for outbound requests.
    /// Used by HttpClient configurations across the application.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Returns a normalized TimeSpan for HttpClient timeouts.
    /// Clamped between 1s and 300s to avoid extreme values.
    /// </summary>
    public TimeSpan HttpTimeout =>
        TimeSpan.FromSeconds(Math.Clamp(HttpTimeoutSeconds, 1, 300));

    /// <summary>
    /// Normalize values and clamp to safe ranges.
    /// </summary>
    public void Normalize()
    {
        if (HttpTimeoutSeconds <= 0) HttpTimeoutSeconds = 60;
        if (HttpTimeoutSeconds > 300) HttpTimeoutSeconds = 300;
    }

    /// <summary>
    /// Validates timeout configuration.
    /// </summary>
    public void Validate()
    {
        if (HttpTimeoutSeconds <= 0)
            throw new ArgumentException("HttpTimeoutSeconds must be greater than 0.", nameof(HttpTimeoutSeconds));
    }
}