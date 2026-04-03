namespace AeroponicIOT.Services.Security;

public static class DeviceIdentityNormalizer
{
    public static bool TryNormalizeMac(string? macAddress, out string normalizedMac)
    {
        normalizedMac = string.Empty;
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            return false;
        }

        normalizedMac = macAddress.Trim().ToUpperInvariant();
        return true;
    }

    public static string NormalizeMac(string macAddress)
    {
        return macAddress.Trim().ToUpperInvariant();
    }
}