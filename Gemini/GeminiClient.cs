using Microsoft.Extensions.Options;
using AiTranslatorDotnet.KeyManagement;
using AiTranslatorDotnet.Configuration.Options;

namespace AiTranslatorDotnet.Gemini
{
    /// <summary>
    /// Minimal Gemini client that attempts a request with a shuffled sequence of API keys.
    /// For each translation call, a fresh ApiKeyRotator is created from .env.local (or environment variable).
    /// If one key fails (401/403/429/503 or network/timeout), it moves on to the next key.
    /// </summary>
    public sealed class GeminiClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<GeminiClient> _logger;
        private readonly GeminiOptions _options;

        public GeminiClient(
            HttpClient http,
            IOptions<GeminiOptions> options,
            ILogger<GeminiClient> logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new GeminiOptions();

            // Note: Do not set the base address here; BuildRequestUri() composes full URL each time
            // to allow per-call model overrides in the future if needed.
        }

        /// <summary>
        /// Translates the input text from sourceLang (optional) to targetLang using Gemini 2.0 Flash.
        /// This method will iterate through API keys until one succeeds or all keys are exhausted.
        /// </summary>
        public async Task<string> TranslateAsync(string text, string? sourceLang, string targetLang, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Input text cannot be null or empty.", nameof(text));
            if (string.IsNullOrWhiteSpace(targetLang))
                throw new ArgumentException("Target language cannot be null or empty.", nameof(targetLang));

            var rotator = ApiKeyRotator.FromEnv();
            Exception? lastError = null;

            while (rotator.TryGetNext(out var apiKey))
            {
                try
                {
                    var requestUri = BuildRequestUri();
                    using var req = new HttpRequestMessage(HttpMethod.Post, requestUri);

                    // Inject the current API key (never log the actual key)
                    req.Headers.TryAddWithoutValidation("X-goog-api-key", apiKey);

                    // Build request body compatible with the Gemini generateContent endpoint
                    // (implementation lives in GeminiRequestFactory; provided in a later step)
                    req.Content = GeminiRequestFactory.BuildTranslateRequest(text, sourceLang, targetLang);

                    using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                        // Map Gemini JSON payload to a plain translated string
                        // (implementation lives in GeminiResponseMapper; provided in a later step)
                        var translated = GeminiResponseMapper.MapToTranslatedText(json);
                        return translated;
                    }

                    // Non-success: decide whether to try the next key or fail fast.
                    var status = (int)resp.StatusCode;
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Likely key/quotas/transient issues -> try next key
                    if (status == 401 || status == 403 || status == 429 || status == 503)
                    {
                        lastError = new HttpRequestException(
                            $"Gemini API returned {status} ({resp.ReasonPhrase}). Will try next key.");
                        _logger.LogWarning("Gemini call failed with status {Status}. Reason: {Reason}. Trying next key.", status, resp.ReasonPhrase);
                        continue;
                    }

                    // Other errors -> consider not key-specific; fail immediately
                    throw new HttpRequestException($"Gemini API error {status}: {body}");
                }
                catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
                {
                    // Timeout or client-side cancellation not initiated by caller -> try next key
                    lastError = oce;
                    _logger.LogWarning(oce, "Gemini request timed out or was canceled internally. Trying next key.");
                    continue;
                }
                catch (HttpRequestException hre)
                {
                    // Network or HTTP pipeline error -> try next key
                    lastError = hre;
                    _logger.LogWarning(hre, "Gemini request failed due to HTTP/network error. Trying next key.");
                    continue;
                }
                catch (Exception ex)
                {
                    // Unknown error -> try next key as a defensive fallback
                    lastError = ex;
                    _logger.LogWarning(ex, "Gemini request failed due to an unexpected error. Trying next key.");
                    continue;
                }
            }

            // No more keys left
            throw new InvalidOperationException(
                $"All API keys exhausted. Last error: {lastError?.Message}", lastError);
        }

        /// <summary>
        /// Composes the full request URI:
        /// {BaseUrl}/v1beta/models/{model}:generateContent
        /// </summary>
        private string BuildRequestUri()
        {
            var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
                ? "https://generativelanguage.googleapis.com"
                : _options.BaseUrl.TrimEnd('/');

            var model = string.IsNullOrWhiteSpace(_options.Model)
                ? "gemini-2.0-flash"
                : _options.Model;

            return $"{baseUrl}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        }
    }
}