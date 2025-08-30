using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Security.Turnstile
{
    /// <summary>
    /// Exposes a tiny endpoint to provide the Turnstile site key (public) and the header name
    /// that the client must use to send the token. This is consumed by the custom Swagger UI.
    /// </summary>
    public static class TurnstileEndpoints
    {
        /// <summary>
        /// Maps GET /_turnstile/sitekey -> { siteKey, headerName }.
        /// </summary>
        public static IEndpointRouteBuilder MapTurnstileEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/_turnstile/sitekey", (IOptions<TurnstileOptions> options) =>
            {
                var opt = options.Value;
                var header = string.IsNullOrWhiteSpace(opt.HeaderName) ? "CF-Turnstile-Token" : opt.HeaderName;

                // SiteKey is public by design; do not return SecretKey here.
                return Results.Ok(new
                {
                    siteKey = opt.SiteKey ?? string.Empty,
                    headerName = header
                });
            })
            .WithName("TurnstileSiteKey")
            .WithTags("Turnstile");

            return app;
        }
    }
}