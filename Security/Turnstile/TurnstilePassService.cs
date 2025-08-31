using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Security.Turnstile
{
    /// <summary>
    /// Options for short-lived verification passes.
    /// The pass allows skipping Turnstile for a limited time or number of uses.
    /// </summary>
    public sealed class TurnstilePassOptions
    {
        /// <summary>
        /// Enable/disable the pass feature globally. Default: true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Absolute expiration in seconds for a pass. Default: 300 (5 minutes).
        /// </summary>
        public int ExpirySeconds { get; set; } = 300;

        /// <summary>
        /// Maximum number of times a pass can be consumed. Default: 3.
        /// </summary>
        public int MaxUses { get; set; } = 3;

        /// <summary>
        /// Header name carrying the pass from the client. Default: X-Turnstile-Pass.
        /// </summary>
        public string HeaderName { get; set; } = "X-Turnstile-Pass";

        /// <summary>
        /// Bind the pass to the client's IP address to reduce theft risk. Default: true.
        /// </summary>
        public bool BindToIp { get; set; } = true;

        /// <summary>
        /// Bind the pass to the client's User-Agent to reduce theft risk. Default: true.
        /// </summary>
        public bool BindToUserAgent { get; set; } = true;
    }

    /// <summary>
    /// Issues and validates short-lived verification passes backed by IMemoryCache.
    /// Each pass is opaque (random), bound to a "subject key" (e.g., IP|UA), and has a max-uses counter.
    /// </summary>
    public sealed class TurnstilePassService
    {
        private readonly IMemoryCache _cache;
        private readonly IOptions<TurnstilePassOptions> _options;

        public TurnstilePassService(IMemoryCache cache, IOptions<TurnstilePassOptions> options)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Issues a new pass tied to the current request "subject" (IP/UA) with configured TTL and max uses.
        /// Returns the opaque token string the client should send in the configured header.
        /// </summary>
        public string IssueFor(HttpContext httpContext)
        {
            var opts = _options.Value;
            if (!opts.Enabled) throw new InvalidOperationException("TurnstilePass is disabled.");

            var subjectKey = BuildSubjectKey(httpContext, opts);
            var token = GenerateToken();

            var record = new PassRecord(
                subjectKey: subjectKey,
                expiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(opts.ExpirySeconds, 10, 3600)),
                remainingUses: Math.Clamp(opts.MaxUses, 1, 50));

            // Cache entry absolute expiration equals record.expiresAtUtc
            _cache.Set(token, record, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = record.ExpiresAtUtc
            });

            return token;
        }

        /// <summary>
        /// Attempts to consume a pass from the request.
        /// Returns true if a valid (not expired, subject-bound, remaining-uses&gt;0) pass is presented; false otherwise.
        /// When valid, decrements the remaining-uses counter (and evicts when it reaches 0).
        /// </summary>
        public bool TryConsume(HttpContext httpContext, out string? reason)
        {
            reason = null;

            var opts = _options.Value;
            if (!opts.Enabled) return false;

            // Read token from header
            string? token = null;
            if (httpContext.Request.Headers.TryGetValue(opts.HeaderName, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                token = v.ToString();
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                reason = "missing-pass";
                return false;
            }

            if (!_cache.TryGetValue<PassRecord>(token!, out var record))
            {
                reason = "unknown-or-expired-pass";
                return false;
            }

            // Check subject binding
            var expectedSubject = record.SubjectKey;
            var actualSubject = BuildSubjectKey(httpContext, opts);
            if (!string.Equals(expectedSubject, actualSubject, StringComparison.Ordinal))
            {
                reason = "subject-mismatch";
                return false;
            }

            // Check expiration
            if (DateTimeOffset.UtcNow >= record.ExpiresAtUtc)
            {
                _cache.Remove(token!);
                reason = "expired-pass";
                return false;
            }

            // Decrement uses atomically
            var left = record.Decrement();
            if (left <= 0)
            {
                _cache.Remove(token!);
            }

            return true;
        }

        private static string BuildSubjectKey(HttpContext ctx, TurnstilePassOptions opts)
        {
            var ip = opts.BindToIp ? (ctx.Connection.RemoteIpAddress?.ToString() ?? "noip") : "noip";
            var ua = opts.BindToUserAgent ? (ctx.Request.Headers.UserAgent.ToString() ?? "noua") : "noua";
            var raw = $"{ip}|{ua}";
            return Sha256Base64Url(raw);
        }

        private static string GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64Url(bytes);
        }

        private static string Sha256Base64Url(string input)
        {
            var data = Encoding.UTF8.GetBytes(input);
            Span<byte> hash = stackalloc byte[32];
            using var sha = SHA256.Create();
            sha.TryComputeHash(data, hash, out _);
            return Base64Url(hash);
        }

        private static string Base64Url(ReadOnlySpan<byte> bytes)
        {
            // base64url without padding
            string base64 = Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
            return base64;
        }

        private sealed class PassRecord
        {
            public PassRecord(string subjectKey, DateTimeOffset expiresAtUtc, int remainingUses)
            {
                SubjectKey = subjectKey;
                ExpiresAtUtc = expiresAtUtc;
                _remainingUses = remainingUses;
            }

            public string SubjectKey { get; }
            public DateTimeOffset ExpiresAtUtc { get; }
            private int _remainingUses;

            public int Decrement() => System.Threading.Interlocked.Decrement(ref _remainingUses);
        }
    }

    /// <summary>
    /// DI helper for TurnstilePass.
    /// Configuration section: "TurnstilePass"
    /// Example:
    /// {
    ///   "TurnstilePass": { "Enabled": true, "ExpirySeconds": 300, "MaxUses": 3, "HeaderName": "X-Turnstile-Pass" }
    /// }
    /// </summary>
    public static class TurnstilePassDependencyInjection
    {
        public static IServiceCollection AddTurnstilePass(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMemoryCache();

            services
                .AddOptions<TurnstilePassOptions>()
                .Bind(configuration.GetSection("TurnstilePass"))
                .PostConfigure(o =>
                {
                    if (string.IsNullOrWhiteSpace(o.HeaderName))
                        o.HeaderName = "X-Turnstile-Pass";
                    if (o.ExpirySeconds <= 0) o.ExpirySeconds = 300;
                    if (o.MaxUses <= 0) o.MaxUses = 3;
                });

            services.AddSingleton<TurnstilePassService>();
            return services;
        }
    }
}