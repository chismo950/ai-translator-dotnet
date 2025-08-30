using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Security.Turnstile
{
    /// <summary>
    /// Verifies Cloudflare Turnstile tokens with the siteverify endpoint.
    /// Docs: https://developers.cloudflare.com/turnstile/reference/testing/#verifying-the-user-response
    /// </summary>
    public sealed class TurnstileVerifier
    {
        private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly HttpClient _http;
        private readonly ILogger<TurnstileVerifier> _logger;
        private readonly TurnstileOptions _options;

        public TurnstileVerifier(
            HttpClient http,
            IOptions<TurnstileOptions> options,
            ILogger<TurnstileVerifier> logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

            // Defaults suitable for a small verification call
            if (_http.Timeout == default)
                _http.Timeout = TimeSpan.FromSeconds(5);

            if (_http.DefaultRequestHeaders.Accept.Count == 0)
                _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Verifies a Turnstile token. Returns a structured result rather than throwing on verification failure.
        /// Throws only for client-side issues (e.g., network errors) when the request cannot be completed.
        /// </summary>
        /// <param name="token">The token from the client (widget).</param>
        /// <param name="remoteIp">Optional end-user IP for extra risk signals.</param>
        public async Task<TurnstileVerifyResult> VerifyAsync(string? token, string? remoteIp = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.SecretKey))
                throw new InvalidOperationException("Turnstile SecretKey is not configured.");

            if (string.IsNullOrWhiteSpace(token))
            {
                return TurnstileVerifyResult.Failed("missing-input-response");
            }

            using var content = new FormUrlEncodedContent(BuildForm(_options.SecretKey, token, remoteIp));
            using var resp = await _http.PostAsync(VerifyUrl, content, ct).ConfigureAwait(false);

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile verify HTTP {Status}: {Body}", (int)resp.StatusCode, body);
                return TurnstileVerifyResult.Failed($"http_error_{(int)resp.StatusCode}");
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<TurnstileVerifyResponse>(body, JsonOptions)
                             ?? new TurnstileVerifyResponse();

                if (parsed.Success)
                {
                    return TurnstileVerifyResult.Success(
                        hostname: parsed.Hostname,
                        challengeTs: parsed.ChallengeTs,
                        action: parsed.Action,
                        cdata: parsed.CData);
                }

                // When not successful, return error codes from Turnstile
                return TurnstileVerifyResult.Failed(parsed.ErrorCodes);
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex, "Failed to parse Turnstile verify response: {Body}", body);
                return TurnstileVerifyResult.Failed("invalid-json");
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> BuildForm(string secret, string token, string? remoteIp)
        {
            yield return new("secret", secret);
            yield return new("response", token);

            if (!string.IsNullOrWhiteSpace(remoteIp))
                yield return new("remoteip", remoteIp);
        }

        // --- Internal response/DTO types ---

        private sealed class TurnstileVerifyResponse
        {
            public bool Success { get; set; }
            public string? ChallengeTs { get; set; }  // RFC3339 timestamp string
            public string? Hostname { get; set; }
            public string? Action { get; set; }
            public string? CData { get; set; }

            // Cloudflare returns snake_case; System.Text.Json with CamelCase policy maps it automatically if names match when lower-cased
            public string[] ErrorCodes { get; set; } = Array.Empty<string>();
        }
    }

    /// <summary>
    /// Result object returned by TurnstileVerifier.
    /// </summary>
    public sealed class TurnstileVerifyResult
    {
        private TurnstileVerifyResult() { }

        public bool IsValid { get; private set; }
        public string[] Errors { get; private set; } = Array.Empty<string>();
        public string? Hostname { get; private set; }
        public string? ChallengeTimestamp { get; private set; }
        public string? Action { get; private set; }
        public string? CData { get; private set; }

        public static TurnstileVerifyResult Success(string? hostname, string? challengeTs, string? action, string? cdata)
            => new()
            {
                IsValid = true,
                Hostname = hostname,
                ChallengeTimestamp = challengeTs,
                Action = action,
                CData = cdata
            };

        public static TurnstileVerifyResult Failed(params string[] errors)
            => new()
            {
                IsValid = false,
                Errors = errors is { Length: > 0 } ? errors : new[] { "unknown-error" }
            };
    }
}