using System.Diagnostics;
using System.Diagnostics.Metrics;
using AeroponicIOT.Options;
using Microsoft.Extensions.Options;

namespace AeroponicIOT.Middleware;

public sealed class PerformanceBudgetMiddleware
{
    public const string MeterName = "AeroponicIOT.PerformanceBudgets";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Histogram<double> EndpointDurationMs = Meter.CreateHistogram<double>(
        "aeroponic.endpoint.duration.ms",
        unit: "ms",
        description: "Endpoint duration in milliseconds for budgeted endpoints.");
    private static readonly Counter<long> BudgetViolations = Meter.CreateCounter<long>(
        "aeroponic.endpoint.budget_violations",
        description: "Number of requests that exceeded configured latency budget.");

    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceBudgetMiddleware> _logger;
    private readonly PerformanceBudgetOptions _budgets;

    public PerformanceBudgetMiddleware(
        RequestDelegate next,
        ILogger<PerformanceBudgetMiddleware> logger,
        IOptions<PerformanceBudgetOptions> budgets)
    {
        _next = next;
        _logger = logger;
        _budgets = budgets.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryGetBudget(context.Request, out var endpointName, out var budgetMs))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        await _next(context);
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var tags = new TagList
        {
            { "endpoint", endpointName },
            { "method", context.Request.Method },
            { "status_code", context.Response.StatusCode }
        };

        EndpointDurationMs.Record(elapsedMs, tags);

        if (elapsedMs <= budgetMs)
        {
            return;
        }

        BudgetViolations.Add(1, tags);
        _logger.LogWarning(
            "Performance budget exceeded for {Endpoint}. DurationMs={DurationMs:F2}, BudgetMs={BudgetMs}, StatusCode={StatusCode}",
            endpointName,
            elapsedMs,
            budgetMs,
            context.Response.StatusCode);
    }

    private bool TryGetBudget(HttpRequest request, out string endpointName, out int budgetMs)
    {
        endpointName = string.Empty;
        budgetMs = 0;

        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && request.Path.Equals("/api/dashboard/latest", StringComparison.OrdinalIgnoreCase))
        {
            endpointName = "dashboard.latest";
            budgetMs = _budgets.DashboardLatestP95Ms;
            return true;
        }

        if (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && request.Path.Equals("/api/sensor", StringComparison.OrdinalIgnoreCase))
        {
            endpointName = "sensor.ingest";
            budgetMs = _budgets.SensorIngestP95Ms;
            return true;
        }

        return false;
    }
}