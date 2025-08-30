using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiTranslatorDotnet.Gemini
{
    /// <summary>
    /// Builds HTTP request payloads for Gemini generateContent endpoint.
    /// Focused on translation use case.
    /// </summary>
    public static class GeminiRequestFactory
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Creates a JSON body for translation and returns it as StringContent (application/json).
        /// The prompt instructs Gemini to translate only, returning the translated text with no extra commentary.
        /// </summary>
        /// <param name="text">Input text to translate.</param>
        /// <param name="sourceLang">
        /// Optional source language (e.g., "en", "zh-CN"). If null/empty, the model should auto-detect.
        /// </param>
        /// <param name="targetLang">Target language code/name (e.g., "en", "de", "ja").</param>
        /// <param name="temperature">
        /// Optional generation temperature. For deterministic translation, a low value is recommended.
        /// </param>
        public static HttpContent BuildTranslateRequest(
            string text,
            string? sourceLang,
            string targetLang,
            double temperature = 0.2)
        {
            var prompt = BuildTranslationPrompt(text, sourceLang, targetLang);

            // Minimal payload compatible with:
            // POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = ClampTemperature(temperature),
                    candidateCount = 1
                }
                // You can extend with "safetySettings" or "systemInstruction" later if needed.
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        /// <summary>
        /// Builds the instruction text for translation.
        /// Keep it explicit to avoid explanations or metadata in the response.
        /// </summary>
        private static string BuildTranslationPrompt(string text, string? sourceLang, string targetLang)
        {
            var src = string.IsNullOrWhiteSpace(sourceLang) ? "auto-detect" : sourceLang!.Trim();
            var tgt = targetLang.Trim();

            // Use a clear delimiter to avoid the model mixing instructions with the user text.
            // Avoid Markdown code fences to reduce accidental formatting changes.
            return
$@"You are a professional translator.

TASK:
- Translate the USER_TEXT below {FormatSrc(src)} into {tgt}.
- If the source language is set to auto-detect, first infer it reliably.
- Return ONLY the translated text. Do not add quotes, notes, or explanations.
- Preserve line breaks, punctuation, emoji, numbers, URLs, and placeholders (e.g., {{name}}, {{0}}).
- Keep code snippets and inline code as-is.
- Do not transliterate proper nouns unless context clearly requires it.
- If the input already appears to be in the target language, rephrase minimally to sound natural.

USER_TEXT:
<<<BEGIN_TEXT
{text}
END_TEXT>>>";

            static string FormatSrc(string src)
                => src.Equals("auto-detect", System.StringComparison.OrdinalIgnoreCase)
                    ? "(auto-detected source language)"
                    : $"from {src}";
        }

        private static double ClampTemperature(double value)
        {
            // Gemini typically expects temperature in [0, 2] depending on model;
            // keep it in a conservative range for translation determinism.
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}