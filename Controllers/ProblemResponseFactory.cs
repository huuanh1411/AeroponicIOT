using Microsoft.AspNetCore.Mvc;

namespace AeroponicIOT.Controllers;

internal static class ProblemResponseFactory
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    public static ObjectResult Create(ControllerBase controller, int statusCode, string title, string detail, string? errorCode = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = controller.HttpContext.Request.Path
        };

        problemDetails.Extensions["errorCode"] = errorCode ?? GetDefaultErrorCode(statusCode);
        problemDetails.Extensions["correlationId"] = ResolveCorrelationId(controller.HttpContext);

        var result = new ObjectResult(problemDetails)
        {
            StatusCode = statusCode
        };

        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var responseHeaderValue = context.Response.Headers[CorrelationHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(responseHeaderValue))
        {
            return responseHeaderValue;
        }

        var requestHeaderValue = context.Request.Headers[CorrelationHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestHeaderValue))
        {
            return requestHeaderValue;
        }

        return context.TraceIdentifier;
    }

    private static string GetDefaultErrorCode(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "bad_request",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status422UnprocessableEntity => "validation_failed",
            StatusCodes.Status429TooManyRequests => "rate_limited",
            StatusCodes.Status500InternalServerError => "internal_error",
            StatusCodes.Status503ServiceUnavailable => "service_unavailable",
            _ => "request_failed"
        };
    }
}