using System.Security.Claims;

namespace AeroponicIOT.Services.Security;

public sealed class HttpCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUserContext GetCurrentUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            return new CurrentUserContext(null, null);
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        int? userId = int.TryParse(userIdClaim, out var parsedUserId)
            ? parsedUserId
            : null;

        var role = user.FindFirst(ClaimTypes.Role)?.Value
            ?? user.FindFirst("role")?.Value;

        return new CurrentUserContext(userId, role);
    }
}