
namespace AiTranslatorDotnet.Configuration.Options
{
    /// <summary>
    /// Options for configuring Gemini calls and HTTP behavior.
    /// Bind this from configuration section "Gemini" (e.g., appsettings.json).
    /// </summary>
    public sealed class GeminiOptions
    {
        /// <summary>
        /// Configuration section name used when binding from IConfiguration.
        /// Example:
        /// { "Gemini": { "BaseUrl": "...", "Model": "...", ... } }
        /// </summary>
        public const string SectionName = "Gemini";

        /// <summary>
        /// Base URL for Google Generative Language API.
        /// Default: https://generativelanguage.googleapis.com
        /// </summary>
        public string? BaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

        /// <summary>
        /// Model name to use for translation.
        /// Default: gemini-2.0-flash
        /// </summary>
        public string Model { get; set; } = "gemini-2.0-flash";

        /// <summary>
        /// Default temperature for translation prompts.
        /// Keep low for deterministic outputs. Range [0, 1].
        /// </summary>
        public double Temperature { get; set; } = 0.2;

        /// <summary>
        /// HTTP timeout (in seconds) for the outbound Gemini request.
        /// This will typically be applied to the HttpClient configured for the Gemini client.
        /// </summary>
        public int HttpTimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// Optional maximum input length (characters) you allow per call before rejecting.
        /// This is an application-level guardrail (not enforced by Gemini itself here).
        /// Set 0 or negative to disable.
        /// </summary>
        public int MaxInputChars { get; set; } = 0;

        /// <summary>
        /// Returns a normalized TimeSpan for HttpClient timeouts.
        /// Clamped between 1s and 100s to avoid extreme values.
        /// </summary>
        public TimeSpan HttpTimeout =>
            TimeSpan.FromSeconds(Math.Clamp(HttpTimeoutSeconds, 1, 100));

        /// <summary>
        /// Normalize values and clamp to safe ranges.
        /// Call this after binding, before use.
        /// </summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
                BaseUrl = "https://generativelanguage.googleapis.com";

            if (string.IsNullOrWhiteSpace(Model))
                Model = "gemini-2.0-flash";

            if (Temperature < 0) Temperature = 0;
            if (Temperature > 1) Temperature = 1;

            if (HttpTimeoutSeconds <= 0) HttpTimeoutSeconds = 15;
        }

        /// <summary>
        /// Validates required fields and throws if invalid.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
                throw new ArgumentException("Gemini BaseUrl cannot be null or empty.", nameof(BaseUrl));

            if (string.IsNullOrWhiteSpace(Model))
                throw new ArgumentException("Gemini Model cannot be null or empty.", nameof(Model));
        }

        /// <summary>
        /// Convenience copy.
        /// </summary>
        public GeminiOptions Clone()
            => new()
            {
                BaseUrl = BaseUrl,
                Model = Model,
                Temperature = Temperature,
                HttpTimeoutSeconds = HttpTimeoutSeconds,
                MaxInputChars = MaxInputChars
            };
    }
}