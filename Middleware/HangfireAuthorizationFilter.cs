using Hangfire.Dashboard;

namespace AeroponicIOT.Middleware;

/// <summary>
/// Hangfire Dashboard authorization filter — restricts access to admin users only.
/// In development, allows all. In production, requires admin role.
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Allow in development
        if (httpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>()
            .IsDevelopment())
        {
            return true;
        }

        // In production, require admin role
        var user = httpContext.User;
        return user?.Identity?.IsAuthenticated == true &&
               user.FindFirst("role")?.Value == "Administrator";
    }
}
