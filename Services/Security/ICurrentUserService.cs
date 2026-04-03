namespace AeroponicIOT.Services.Security;

public interface ICurrentUserService
{
    CurrentUserContext GetCurrentUser();
}

public sealed record CurrentUserContext(int? UserId, string? Role)
{
    public bool IsAuthenticated => UserId.HasValue;

    public bool IsAdministrator => string.Equals(Role, "Administrator", StringComparison.OrdinalIgnoreCase);
}