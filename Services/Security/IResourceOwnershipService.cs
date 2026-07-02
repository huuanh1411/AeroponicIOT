using AeroponicIOT.Models;

namespace AeroponicIOT.Services.Security;

/// <summary>
/// Centralized service for checking resource ownership and authorization.
/// Eliminates repeated ownership checks across controllers.
/// </summary>
public interface IResourceOwnershipService
{
    /// <summary>
    /// Check if current user can access a device (owns it or is admin).
    /// </summary>
    /// <returns>true if authorized; false otherwise</returns>
    bool CanAccessDevice(Device device, CurrentUserContext currentUser);

    /// <summary>
    /// Check if current user can access an automation rule (owns the device or is admin).
    /// </summary>
    bool CanAccessAutomationRule(AutomationRule rule, CurrentUserContext currentUser);

    /// <summary>
    /// Check if current user can access a garden (owns any device in it or is admin).
    /// </summary>
    bool CanAccessGarden(Garden garden, CurrentUserContext currentUser);
}
