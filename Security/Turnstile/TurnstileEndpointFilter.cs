using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Security.Turnstile
{
    /// <summary>
    /// Minimal API endpoint filter that enforces Cloudflare Turnstile verification.
    /// Reads the token from a request header (default: "CF-Turnstile-Token") and
    /// verifies it via TurnstileVerifier before allowing the request to proceed.
    ///
    /// Usage (in your endpoint mapping code):
    ///     group.MapPost("/v1/translate", TranslateAsync)
    ///          .RequireTurnstile();
    ///
    /// Enforcement is controlled by TurnstileOptions.
    /// </summary>
    public sealed class TurnstileEndpointFilter : IEndpointFilter
    {
        private readonly TurnstileVerifier _verifier;
        private readonly IOptions<TurnstileOptions> _options;
        private readonly ILogger<TurnstileEndpointFilter> _logger;

        public TurnstileEndpointFilter(
            TurnstileVerifier verifier,
            IOptions<TurnstileOptions> options,
            ILogger<TurnstileEndpointFilter> logger)
        {
            _verifier = verifier;
            _options = options;
            _logger = logger;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var opts = _options.Value;

            // Feature toggle: skip when disabled.
            if (!opts.RequireOnTranslate)
                return await next(context);

            // Skip (with warning) if SecretKey is not configured to avoid breaking local runs.
            if (string.IsNullOrWhiteSpace(opts.SecretKey))
            {
                _logger.LogWarning("Turnstile SecretKey is not configured; skipping verification.");
                return await next(context);
            }

            var headerName = string.IsNullOrWhiteSpace(opts.HeaderName) ? "CF-Turnstile-Token" : opts.HeaderName;

            string? token = null;
            if (http.Request.Headers.TryGetValue(headerName, out var values) && !string.IsNullOrWhiteSpace(values))
                token = values.ToString();

            var remoteIp = http.Connection.RemoteIpAddress?.ToString();

            var result = await _verifier.VerifyAsync(token, remoteIp, http.RequestAborted).ConfigureAwait(false);

            if (!result.IsValid)
            {
                // Missing token -> 400; present but invalid -> 403.
                var status = token is null ? StatusCodes.Status400BadRequest : StatusCodes.Status403Forbidden;

                return Results.Problem(
                    title: token is null ? "Turnstile token is missing." : "Turnstile verification failed.",
                    detail: result.Errors is { Length: > 0 } ? string.Join(",", result.Errors) : "unknown-error",
                    statusCode: status,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errors"] = result.Errors,
                        ["remoteIp"] = remoteIp
                    });
            }

            return await next(context);
        }
    }

    /// <summary>
    /// Extensions to conveniently apply the filter with DI-resolved dependencies.
    /// </summary>
    public static class TurnstileEndpointFilterExtensions
    {
        /// <summary>
        /// Adds a filter-factory that resolves Turnstile dependencies from DI and enforces verification.
        /// </summary>
        public static RouteHandlerBuilder RequireTurnstile(this RouteHandlerBuilder builder)
        {
            return builder.AddEndpointFilterFactory((factoryContext, next) =>
            {
                var sp = factoryContext.ApplicationServices;

                var verifier = sp.GetRequiredService<TurnstileVerifier>();
                var options = sp.GetRequiredService<IOptions<TurnstileOptions>>();
                var logger = sp.GetRequiredService<ILogger<TurnstileEndpointFilter>>();

                var filter = new TurnstileEndpointFilter(verifier, options, logger);
                return ctx => filter.InvokeAsync(ctx, next);
            });
        }
    }
}