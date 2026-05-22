namespace DSCC.Orbbec;

public sealed record OrbbecDeviceInfo(
    OrbbecDeviceType DeviceType,
    string Serial,
    OrbbecConnectionKind Connection,
    string DisplayName)
{
    public string? FirmwareVersion { get; init; }

    public string? HardwareVersion { get; init; }

    public string? IpAddress { get; init; }

    public string? NativeName { get; init; }

    public string? SupportedMinSdkVersion { get; init; }

    public bool IsPlaceholder { get; init; }
}
