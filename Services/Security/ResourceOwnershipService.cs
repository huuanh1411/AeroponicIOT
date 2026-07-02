using AeroponicIOT.Models;

namespace AeroponicIOT.Services.Security;

/// <inheritdoc />
public sealed class ResourceOwnershipService : IResourceOwnershipService
{
    public bool CanAccessDevice(Device device, CurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        if (currentUser.IsAdministrator)
            return true;

        return device.UserId == currentUser.UserId;
    }

    public bool CanAccessAutomationRule(AutomationRule rule, CurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        if (currentUser.IsAdministrator)
            return true;

        if (rule.Device == null)
            return false;

        return rule.Device.UserId == currentUser.UserId;
    }

    public bool CanAccessGarden(Garden garden, CurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        if (currentUser.IsAdministrator)
            return true;

        // Farmers can access garden if they own at least one device in it
        return garden.Devices?.Any(d => d.UserId == currentUser.UserId) ?? false;
    }
}
