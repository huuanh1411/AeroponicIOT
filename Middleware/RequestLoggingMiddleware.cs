using System.Diagnostics;

namespace AeroponicIOT.Middleware;

public partial class RequestLoggingMiddleware
{
    private const string CorrelationHeaderName = "X-Correlation-ID";
    private static readonly string[] ExcludedPathPrefixes =
    {
        "/health",
        "/favicon.ico",
        "/css",
        "/js",
        "/lib"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    [LoggerMessage(
        EventId = 1,
        Message = "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms. CorrelationId={CorrelationId}, User={User}, RemoteIp={RemoteIp}")]
    private static partial void LogHttpRequest(
        ILogger logger,
        LogLevel logLevel,
        string method,
        string path,
        int statusCode,
        double elapsedMs,
        string correlationId,
        string user,
        string remoteIp);

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var statusCode = context.Response.StatusCode;
            var logLevel = statusCode >= 500 ? LogLevel.Error : LogLevel.Information;

            if (_logger.IsEnabled(logLevel))
            {
                var elapsedMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2);
                var correlationId = context.Response.Headers[CorrelationHeaderName].FirstOrDefault()
                    ?? context.Request.Headers[CorrelationHeaderName].FirstOrDefault()
                    ?? context.TraceIdentifier;
                var remoteIp = context.Connection.RemoteIpAddress is { } remoteIpAddress
                    ? remoteIpAddress.ToString()
                    : "unknown";

                LogHttpRequest(_logger,
                    logLevel,
                    context.Request.Method,
                    path.ToString(),
                    statusCode,
                    elapsedMs,
                    correlationId,
                    context.User.Identity?.Name ?? "anonymous",
                    remoteIp);
            }
        }
    }

    private static bool ShouldSkip(PathString path)
    {
        if (!path.HasValue)
        {
            return true;
        }

        return ExcludedPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
