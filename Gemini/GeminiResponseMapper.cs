using System.Text.Json;

namespace AiTranslatorDotnet.Gemini
{
    /// <summary>
    /// Maps Gemini generateContent responses to a plain translated string.
    /// Expected shape (v1beta):
    /// {
    ///   "candidates": [
    ///     {
    ///       "content": { "parts": [ { "text": "..." }, ... ] },
    ///       "finishReason": "STOP" | "SAFETY" | ...
    ///     }
    ///   ],
    ///   "promptFeedback": { "blockReason": "SAFETY" | ..., ... }
    /// }
    /// </summary>
    public static class GeminiResponseMapper
    {
        /// <summary>
        /// Extracts the first non-empty text from the first successful candidate.
        /// Throws if no candidates or no text parts are found.
        /// </summary>
        public static string MapToTranslatedText(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Response JSON cannot be null or empty.", nameof(json));

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 1) candidates[]
                if (!root.TryGetProperty("candidates", out var candidatesElem) || candidatesElem.ValueKind != JsonValueKind.Array)
                {
                    var (blockReason, finish) = ExtractDiagnostics(root);
                    throw new InvalidOperationException(BuildNoCandidateMessage(blockReason, finish));
                }

                foreach (var candidate in candidatesElem.EnumerateArray())
                {
                    // Optional: check finishReason; still try to read text if present.
                    string? finishReason = null;
                    if (candidate.TryGetProperty("finishReason", out var finishElem) && finishElem.ValueKind == JsonValueKind.String)
                        finishReason = finishElem.GetString();

                    // 2) content.parts[*].text
                    if (candidate.TryGetProperty("content", out var contentElem) &&
                        contentElem.ValueKind == JsonValueKind.Object &&
                        contentElem.TryGetProperty("parts", out var partsElem) &&
                        partsElem.ValueKind == JsonValueKind.Array)
                    {
                        var aggregated = ExtractFirstNonEmptyText(partsElem);
                        if (!string.IsNullOrEmpty(aggregated))
                        {
                            return aggregated;
                        }
                    }
                }

                // If we reach here, no text found in any candidate
                var (block, finishAll) = ExtractDiagnostics(root);
                throw new InvalidOperationException(BuildNoTextMessage(block, finishAll));
            }
            catch (JsonException jex)
            {
                throw new InvalidOperationException("Failed to parse Gemini response JSON.", jex);
            }
        }

        private static string ExtractFirstNonEmptyText(JsonElement partsArray)
        {
            // Concatenate all "text" fields in order; return first non-empty aggregation
            // For translation we usually expect a single "text" part.
            string buffer = string.Empty;

            foreach (var part in partsArray.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                    continue;

                if (part.TryGetProperty("text", out var textElem) && textElem.ValueKind == JsonValueKind.String)
                {
                    var t = textElem.GetString() ?? string.Empty;
                    buffer += t;
                }
                // Ignore other part types (e.g., inlineData, functionCall) for translation use case.
            }

            buffer = buffer?.Trim() ?? string.Empty;
            return buffer.Length > 0 ? buffer : string.Empty;
        }

        /// <summary>
        /// Attempts to extract helpful diagnostics such as promptFeedback.blockReason
        /// and the first candidate's finishReason.
        /// </summary>
        private static (string? blockReason, string? finishReason) ExtractDiagnostics(JsonElement root)
        {
            string? block = null;
            string? finish = null;

            if (root.TryGetProperty("promptFeedback", out var pf) && pf.ValueKind == JsonValueKind.Object)
            {
                if (pf.TryGetProperty("blockReason", out var br) && br.ValueKind == JsonValueKind.String)
                {
                    block = br.GetString();
                }
            }

            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
            {
                foreach (var cand in candidates.EnumerateArray())
                {
                    if (cand.TryGetProperty("finishReason", out var fr) && fr.ValueKind == JsonValueKind.String)
                    {
                        finish = fr.GetString();
                        break;
                    }
                }
            }

            return (block, finish);
        }

        private static string BuildNoCandidateMessage(string? blockReason, string? finishReason)
        {
            if (!string.IsNullOrEmpty(blockReason) || !string.IsNullOrEmpty(finishReason))
            {
                return $"No candidates returned by Gemini. blockReason={blockReason ?? "n/a"}, finishReason={finishReason ?? "n/a"}.";
            }
            return "No candidates returned by Gemini.";
        }

        private static string BuildNoTextMessage(string? blockReason, string? finishReason)
        {
            if (!string.IsNullOrEmpty(blockReason) || !string.IsNullOrEmpty(finishReason))
            {
                return $"No text found in Gemini candidates. blockReason={blockReason ?? "n/a"}, finishReason={finishReason ?? "n/a"}.";
            }
            return "No text found in Gemini candidates.";
        }
    }
}