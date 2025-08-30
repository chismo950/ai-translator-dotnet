using System.Net.Http.Headers;

namespace AiTranslatorDotnet.Security.Turnstile
{
    /// <summary>
    /// Service registration for Cloudflare Turnstile.
    /// Usage in Program.cs:
    ///   builder.Services.AddTurnstile(builder.Configuration);
    /// </summary>
    public static class DependencyInjection
    {
        public static IServiceCollection AddTurnstile(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddOptions<TurnstileOptions>()
                .Bind(configuration.GetSection("Turnstile"))
                .PostConfigure(o =>
                {
                    // Ensure sane defaults
                    if (string.IsNullOrWhiteSpace(o.HeaderName))
                        o.HeaderName = "CF-Turnstile-Token";
                });
                // Note: we do NOT ValidateOnStart() here to avoid blocking local runs without keys.
                // The verifier will return "missing-input-response" if token/keys are absent.

            services.AddHttpClient<TurnstileVerifier>(http =>
            {
                // Small payload, short timeout
                if (http.Timeout == default)
                    http.Timeout = TimeSpan.FromSeconds(5);

                if (http.DefaultRequestHeaders.Accept.Count == 0)
                    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            return services;
        }
    }
}