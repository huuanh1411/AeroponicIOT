using AeroponicIOT.Middleware;
using AeroponicIOT.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroponicIOT.Tests;

public class PerformanceBudgetMiddlewareTests
{
    [Fact]
    public async Task InvokeAsyncSkipsNonBudgetedEndpoints()
    {
        var logger = new CollectingLogger<PerformanceBudgetMiddleware>();
        var options = Microsoft.Extensions.Options.Options.Create(new PerformanceBudgetOptions
        {
            DashboardLatestP95Ms = 1,
            SensorIngestP95Ms = 1
        });

        var called = false;
        var middleware = new PerformanceBudgetMiddleware(
            async context =>
            {
                called = true;
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            logger,
            options);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/unknown";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task InvokeAsyncLogsWarningWhenBudgetExceeded()
    {
        var logger = new CollectingLogger<PerformanceBudgetMiddleware>();
        var options = Microsoft.Extensions.Options.Options.Create(new PerformanceBudgetOptions
        {
            DashboardLatestP95Ms = 1,
            SensorIngestP95Ms = 1
        });

        var middleware = new PerformanceBudgetMiddleware(
            async context =>
            {
                await Task.Delay(10);
                context.Response.StatusCode = StatusCodes.Status200OK;
            },
            logger,
            options);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/dashboard/latest";

        await middleware.InvokeAsync(context);

        Assert.NotEmpty(logger.Warnings);
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
