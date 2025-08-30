using System.Net.Http.Headers;
using AiTranslatorDotnet.Configuration.Options;
using AiTranslatorDotnet.Gemini;
using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Configuration
{
    /// <summary>
    /// Service registration helpers for the translator API.
    /// Call this once in Program.cs:
    ///     builder.Services.AddGeminiTranslator(builder.Configuration);
    /// </summary>
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers options, HttpClient, and the Gemini client.
        /// </summary>
        public static IServiceCollection AddGeminiTranslator(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind and validate Gemini options
            services
                .AddOptions<GeminiOptions>()
                .Bind(configuration.GetSection(GeminiOptions.SectionName))
                .PostConfigure(o => o.Normalize())
                .Validate(o =>
                {
                    try
                    {
                        o.Validate();
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }, failureMessage: "Invalid Gemini configuration. Check 'Gemini' section in appsettings.");

            // Typed HttpClient for GeminiClient
            services.AddHttpClient<GeminiClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;

                // Timeout is driven by options
                http.Timeout = opts.HttpTimeout;

                // Default headers
                http.DefaultRequestHeaders.Accept.Clear();
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                // Do NOT set the API key header here; it is injected per-request in GeminiClient.
                // Do NOT set BaseAddress; GeminiClient composes the full URL per call.
            });

            return services;
        }
    }
}