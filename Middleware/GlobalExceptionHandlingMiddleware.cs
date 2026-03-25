using AeroponicIOT.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AeroponicIOT.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private const string CorrelationHeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemDetailsAsync(context, ex);
        }
    }

    private async Task WriteProblemDetailsAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail, errorCode, logLevel) = exception switch
        {
            DomainValidationException => (StatusCodes.Status400BadRequest, "Bad Request", exception.Message, "bad_request", LogLevel.Warning),
            ResourceNotFoundException => (StatusCodes.Status404NotFound, "Not Found", exception.Message, "not_found", LogLevel.Information),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request", exception.Message, "bad_request", LogLevel.Warning),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found", exception.Message, "not_found", LogLevel.Information),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "User not authenticated", "unauthorized", LogLevel.Warning),
            DbUpdateException => (StatusCodes.Status409Conflict, "Conflict", "The request could not be completed because of a data conflict", "conflict", LogLevel.Warning),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Conflict", exception.Message, "conflict", LogLevel.Warning),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred", "internal_error", LogLevel.Error)
        };

        _logger.Log(logLevel, exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var correlationId = context.Response.Headers[CorrelationHeaderName].FirstOrDefault()
            ?? context.Request.Headers[CorrelationHeaderName].FirstOrDefault()
            ?? context.TraceIdentifier;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = context.Request.Path
        };

        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problemDetails, cancellationToken: context.RequestAborted);
    }
}
