using Microsoft.AspNetCore.Mvc;

namespace AiTranslatorDotnet.Middleware
{
    /// <summary>
    /// Catches unhandled exceptions, maps them to ProblemDetails, and returns JSON.
    /// Add in Program.cs: app.UseExceptionHandling();
    /// </summary>
    public sealed class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // If the response has already started, rethrow to let the server fail-fast.
                if (context.Response.HasStarted)
                {
                    _logger.LogError(ex, "Unhandled exception after response started.");
                    throw; // legal here (inside catch)
                }

                await HandleExceptionAsync(context, ex).ConfigureAwait(false);
            }
        }

        private async Task HandleExceptionAsync(HttpContext httpContext, Exception ex)
        {
            var problem = MapToProblemDetails(httpContext, ex);

            httpContext.Response.Clear();
            httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            httpContext.Response.ContentType = "application/problem+json";

            // Log with appropriate level
            if (problem.Status >= 500)
                _logger.LogError(ex, "Unhandled server error {Status}: {Title}", problem.Status, problem.Title);
            else
                _logger.LogWarning(ex, "Handled client error {Status}: {Title}", problem.Status, problem.Title);

            await httpContext.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
        }

        private static ProblemDetails MapToProblemDetails(HttpContext ctx, Exception ex)
        {
            // Default 500
            var status = StatusCodes.Status500InternalServerError;
            var title = "An unexpected error occurred.";
            string? detail = ex.Message;

            switch (ex)
            {
                case ArgumentException:
                case FormatException:
                    status = StatusCodes.Status400BadRequest;
                    title = "Invalid request.";
                    break;

                case InvalidOperationException:
                    // Frequently used for "no candidates" or "all keys exhausted" in our flow.
                    status = StatusCodes.Status502BadGateway;
                    title = "Upstream service failed.";
                    break;

                case TaskCanceledException:
                case OperationCanceledException:
                    // Treat as timeout when not initiated by the client.
                    status = StatusCodes.Status504GatewayTimeout;
                    title = "The request timed out.";
                    break;

                case HttpRequestException:
                    status = StatusCodes.Status502BadGateway;
                    title = "Upstream HTTP error.";
                    break;
            }

            return new ProblemDetails
            {
                Type = $"https://httpstatuses.com/{status}",
                Title = title,
                Status = status,
                Detail = detail,
                Instance = ctx.Request?.Path.Value,
                Extensions =
                {
                    ["traceId"] = ctx.TraceIdentifier
                }
            };
        }
    }

    /// <summary>
    /// Registration extensions for ExceptionHandlingMiddleware.
    /// </summary>
    public static class ExceptionHandlingExtensions
    {
        /// <summary>
        /// Globally handle exceptions and return RFC 7807 ProblemDetails.
        /// </summary>
        public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
            => app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}