namespace AeroponicIOT.Models;

public enum DeviceLifecycleStatus
{
    Pending,
    Active,
    Online,
    Offline,
    Inactive
}

public static class DeviceStatusValues
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    public const string Online = "Online";
    public const string Offline = "Offline";
    public const string Inactive = "Inactive";

    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [Pending] = Pending,
        [Active] = Active,
        [Online] = Online,
        [Offline] = Offline,
        [Inactive] = Inactive
    };

    public static IEnumerable<string> All => Allowed.Values.Distinct();

    public static bool IsActive(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Equals(Active, StringComparison.OrdinalIgnoreCase)
            || status.Equals(Online, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNormalize(string? status, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return Allowed.TryGetValue(status.Trim(), out normalized!);
    }
}