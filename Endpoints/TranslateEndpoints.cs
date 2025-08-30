using AiTranslatorDotnet.Configuration.Options;
using AiTranslatorDotnet.Contracts;
using AiTranslatorDotnet.Gemini;
using Microsoft.Extensions.Options;

namespace AiTranslatorDotnet.Endpoints
{
    /// <summary>
    /// Minimal API endpoints for translation.
    /// Register in Program.cs with:
    ///     app.MapTranslateEndpoints();
    /// </summary>
    public static class TranslateEndpoints
    {
        /// <summary>
        /// Maps /v1/translate endpoint.
        /// </summary>
        public static IEndpointRouteBuilder MapTranslateEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/v1").WithTags("Translate");

            group.MapPost("/translate", TranslateAsync)
                 .WithName("Translate")
                 .Produces<TranslateResponse>(StatusCodes.Status200OK)
                 .Produces(StatusCodes.Status400BadRequest)
                 .ProducesProblem(StatusCodes.Status500InternalServerError);

            return app;
        }

        /// <summary>
        /// POST /v1/translate
        /// Request body: TranslateRequest
        /// Response body: TranslateResponse
        /// </summary>
        private static async Task<IResult> TranslateAsync(
            TranslateRequest request,
            GeminiClient gemini,
            IOptions<GeminiOptions> options,
            CancellationToken ct)
        {
            if (request is null)
                return Results.BadRequest("Request body is required.");

            if (string.IsNullOrWhiteSpace(request.Text))
                return Results.BadRequest("Field 'text' is required.");

            if (string.IsNullOrWhiteSpace(request.TargetLang))
                return Results.BadRequest("Field 'targetLang' is required.");

            // Optional application-level guardrail
            var max = options.Value.MaxInputChars;
            if (max > 0 && request.Text.Length > max)
                return Results.BadRequest($"Input text length exceeds MaxInputChars={max}.");

            // Perform translation (keys are rotated inside GeminiClient)
            var translated = await gemini.TranslateAsync(
                text: request.Text,
                sourceLang: request.SourceLang,
                targetLang: request.TargetLang,
                ct: ct);

            var response = new TranslateResponse
            {
                SourceLang = request.SourceLang ?? "auto",
                TargetLang = request.TargetLang,
                TranslatedText = translated
            };

            return Results.Ok(response);
        }
    }
}