namespace DSCC.Orbbec;

public static class OrbbecDeviceAssignmentPolicy
{
    public static bool IsCompatibleWithStationType(OrbbecDeviceInfo deviceInfo, string? configuredDeviceType)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (string.IsNullOrWhiteSpace(configuredDeviceType))
        {
            return true;
        }

        return TryParseDeviceType(configuredDeviceType, out var expectedDeviceType) &&
               deviceInfo.DeviceType == expectedDeviceType;
    }

    public static bool TryParseDeviceType(string? value, out OrbbecDeviceType deviceType)
    {
        var normalized = Normalize(value);
        switch (normalized)
        {
            case "femtomega":
            case "mega":
                deviceType = OrbbecDeviceType.FemtoMega;
                return true;
            case "femtobolt":
            case "bolt":
                deviceType = OrbbecDeviceType.FemtoBolt;
                return true;
            default:
                return Enum.TryParse(value, ignoreCase: true, out deviceType);
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
