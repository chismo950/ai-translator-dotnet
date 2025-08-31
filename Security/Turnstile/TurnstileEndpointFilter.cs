using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Security.Turnstile
{
    /// <summary>
    /// Endpoint filter that enforces Cloudflare Turnstile.
    /// Adds support for a short-lived "pass" that, when valid, allows skipping Turnstile
    /// for a limited time or number of uses.
    /// </summary>
    public sealed class TurnstileEndpointFilter : IEndpointFilter
    {
        private readonly TurnstileVerifier _verifier;
        private readonly IOptions<TurnstileOptions> _options;
        private readonly ILogger<TurnstileEndpointFilter> _logger;

        // Optional pass service and options (enabled via AddTurnstilePass)
        private readonly TurnstilePassService? _passService;
        private readonly IOptions<TurnstilePassOptions>? _passOptions;

        public TurnstileEndpointFilter(
            TurnstileVerifier verifier,
            IOptions<TurnstileOptions> options,
            ILogger<TurnstileEndpointFilter> logger,
            TurnstilePassService? passService = null,
            IOptions<TurnstilePassOptions>? passOptions = null)
        {
            _verifier = verifier;
            _options = options;
            _logger = logger;
            _passService = passService;
            _passOptions = passOptions;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var opts = _options.Value;

            // Feature toggle: when disabled, do not enforce Turnstile (and ignore pass).
            if (!opts.RequireOnTranslate)
                return await next(context);

            // Fail hard if Turnstile is required but SecretKey is missing.
            if (string.IsNullOrWhiteSpace(opts.SecretKey))
            {
                _logger.LogError("Turnstile SecretKey is not configured. Refusing request.");
                return Results.Problem(
                    title: "Turnstile is required but not configured.",
                    detail: "Missing Turnstile SecretKey. Set TURNSTILE__SECRETKEY (env) and restart.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            // ---- STEP 1: Try to consume a short-lived pass (if enabled) ----
            var passEnabled = _passService is not null && _passOptions?.Value?.Enabled == true;
            if (passEnabled)
            {
                if (_passService!.TryConsume(http, out var reason))
                {
                    // Valid pass: skip Turnstile and proceed
                    return await next(context);
                }
            }

            // ---- STEP 2: Enforce Turnstile verification ----
            var headerName = string.IsNullOrWhiteSpace(opts.HeaderName) ? "CF-Turnstile-Token" : opts.HeaderName;

            string? token = null;
            if (http.Request.Headers.TryGetValue(headerName, out var values) && !string.IsNullOrWhiteSpace(values))
                token = values.ToString();

            var remoteIp = http.Connection.RemoteIpAddress?.ToString();

            var result = await _verifier.VerifyAsync(token, remoteIp, http.RequestAborted).ConfigureAwait(false);

            if (!result.IsValid)
            {
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

            // ---- STEP 3: Issue a pass on successful verification (if enabled) ----
            if (passEnabled)
            {
                var passToken = _passService!.IssueFor(http);
                var passHeader = string.IsNullOrWhiteSpace(_passOptions!.Value.HeaderName)
                    ? "X-Turnstile-Pass"
                    : _passOptions.Value.HeaderName;

                // Return the pass to the client; client should present it in subsequent requests.
                http.Response.Headers[passHeader] = passToken;
            }

            return await next(context);
        }
    }

    /// <summary>
    /// Extensions to apply the filter with DI-resolved dependencies.
    /// </summary>
    public static class TurnstileEndpointFilterExtensions
    {
        public static RouteHandlerBuilder RequireTurnstile(this RouteHandlerBuilder builder)
        {
            return builder.AddEndpointFilterFactory((factoryContext, next) =>
            {
                var sp = factoryContext.ApplicationServices;

                var verifier   = sp.GetRequiredService<TurnstileVerifier>();
                var options    = sp.GetRequiredService<IOptions<TurnstileOptions>>();
                var logger     = sp.GetRequiredService<ILogger<TurnstileEndpointFilter>>();

                // Pass service is optional: only present if AddTurnstilePass() was called
                var passSvcOpt = sp.GetService<TurnstilePassService>();
                var passOpt    = sp.GetService<IOptions<TurnstilePassOptions>>();

                var filter = new TurnstileEndpointFilter(verifier, options, logger, passSvcOpt, passOpt);
                return ctx => filter.InvokeAsync(ctx, next);
            });
        }
    }
}