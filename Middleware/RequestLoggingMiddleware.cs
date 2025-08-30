using System.Diagnostics;

namespace AiTranslatorDotnet.Middleware
{
    /// <summary>
    /// Minimal request logging middleware.
    /// Logs method, path, status code, and duration.
    /// Does not log bodies or sensitive headers.
    /// </summary>
    public sealed class RequestLoggingMiddleware
    {
        private const string RequestIdHeader = "X-Request-Id";

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task Invoke(HttpContext context)
        {
            // Ensure a stable request id for correlation
            var requestId = EnsureRequestId(context);

            // Use the ASP.NET Core trace identifier as well
            var traceId = context.TraceIdentifier;

            using var _scope = _logger.BeginScope(new
            {
                traceId,
                requestId
            });

            var sw = Stopwatch.StartNew();
            var method = context.Request?.Method ?? "UNKNOWN";
            var path = context.Request?.Path.Value ?? "/";

            try
            {
                _logger.LogInformation("HTTP {Method} {Path} - started (traceId={TraceId}, requestId={RequestId})",
                    method, path, traceId, requestId);

                await _next(context).ConfigureAwait(false);

                sw.Stop();
                var status = context.Response?.StatusCode ?? 0;

                _logger.LogInformation("HTTP {Method} {Path} -> {Status} in {ElapsedMs} ms",
                    method, path, status, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "HTTP {Method} {Path} -> unhandled exception after {ElapsedMs} ms",
                    method, path, sw.ElapsedMilliseconds);
                throw; // handled by ExceptionHandlingMiddleware
            }
        }

        private static string EnsureRequestId(HttpContext context)
        {
            // If client sent an id, keep it; otherwise generate one and add to response.
            if (context.Request.Headers.TryGetValue(RequestIdHeader, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                // Mirror to response so downstream systems can see it
                context.Response.Headers[RequestIdHeader] = existing.ToString();
                return existing.ToString();
            }

            var id = Guid.NewGuid().ToString("n");
            context.Response.Headers[RequestIdHeader] = id;
            return id;
        }
    }

    /// <summary>
    /// Registration extensions for RequestLoggingMiddleware.
    /// </summary>
    public static class RequestLoggingExtensions
    {
        /// <summary>
        /// Logs basic request/response information.
        /// Place this early in the pipeline (after exception handling).
        /// </summary>
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
            => app.UseMiddleware<RequestLoggingMiddleware>();
    }
}