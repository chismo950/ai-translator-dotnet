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
            // Bind and validate global timeout options
            services
                .AddOptions<TimeoutOptions>()
                .Bind(configuration.GetSection(TimeoutOptions.SectionName))
                .PostConfigure(o => o.Normalize());

            // Bind and validate Gemini options
            services
                .AddOptions<GeminiOptions>()
                .Bind(configuration.GetSection(GeminiOptions.SectionName))
                .PostConfigure(o => o.Normalize());
                // Temporarily removed validation to debug startup hang
                //.Validate(o =>
                //{
                //    try
                //    {
                //        o.Validate();
                //        return true;
                //    }
                //    catch
                //    {
                //        return false;
                //    }
                //}, failureMessage: "Invalid Gemini configuration. Check 'Gemini' section in appsettings.");

            // Typed HttpClient for GeminiClient
            services.AddHttpClient<GeminiClient>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
                var timeoutOpts = sp.GetRequiredService<IOptions<TimeoutOptions>>().Value;

                // Timeout is driven by global timeout configuration with Gemini-specific override
                http.Timeout = opts.GetHttpTimeout(timeoutOpts);

                // Default headers
                http.DefaultRequestHeaders.Accept.Clear();
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                // Do NOT set the API key header here; it is injected per-request in GeminiClient.
                // Do NOT set BaseAddress; GeminiClient composes the full URL per call.
            });

            return services;
        }

        /// <summary>
        /// Registers CORS configuration for allowed domain suffixes.
        /// </summary>
        public static IServiceCollection AddCorsConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind CORS options
            services
                .AddOptions<CorsOptions>()
                .Bind(configuration.GetSection(CorsOptions.SectionName));

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>();
                    
                    policy.SetIsOriginAllowed(origin =>
                    {
                        if (string.IsNullOrEmpty(origin))
                            return false;

                        var uri = new Uri(origin);
                        var host = uri.Host.ToLowerInvariant();

                        return corsOptions?.AllowedOriginSuffixes?.Any(suffix =>
                            host == suffix.ToLowerInvariant() || 
                            host.EndsWith($".{suffix.ToLowerInvariant()}")
                        ) ?? false;
                    });

                    policy.AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials()
                          .WithExposedHeaders("X-Turnstile-Pass");
                });
            });

            return services;
        }
    }
}