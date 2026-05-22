namespace DSCC.Orbbec;

public static class OrbbecFirmwarePolicy
{
    public static string RecommendedFirmwareFor(OrbbecDeviceType deviceType)
    {
        return deviceType switch
        {
            OrbbecDeviceType.FemtoBolt => OrbbecSdkRuntimeProbe.FemtoBoltRecommendedFirmware,
            OrbbecDeviceType.FemtoMega => OrbbecSdkRuntimeProbe.FemtoMegaRecommendedFirmware,
            _ => string.Empty
        };
    }

    public static bool IsRecommendedOrNewer(OrbbecDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        if (string.IsNullOrWhiteSpace(deviceInfo.FirmwareVersion))
        {
            return false;
        }

        return CompareVersions(deviceInfo.FirmwareVersion, RecommendedFirmwareFor(deviceInfo.DeviceType)) >= 0;
    }

    private static int CompareVersions(string actual, string recommended)
    {
        var actualParts = ParseVersion(actual);
        var recommendedParts = ParseVersion(recommended);
        var count = Math.Max(actualParts.Length, recommendedParts.Length);

        for (var index = 0; index < count; index++)
        {
            var left = index < actualParts.Length ? actualParts[index] : 0;
            var right = index < recommendedParts.Length ? recommendedParts[index] : 0;
            var comparison = left.CompareTo(right);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int[] ParseVersion(string value)
    {
        return value
            .Trim()
            .TrimStart('v', 'V')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(new string(part.TakeWhile(char.IsDigit).ToArray()), out var number) ? number : 0)
            .ToArray();
    }
}
