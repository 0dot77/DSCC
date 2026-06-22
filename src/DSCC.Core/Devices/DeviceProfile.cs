namespace DSCC.Core.Devices;

public sealed class DeviceProfile
{
    public const string DefaultDeviceType = "FemtoMega";

    public string DeviceType { get; set; } = DefaultDeviceType;

    public string Serial { get; set; } = string.Empty;

    public int? StationId { get; set; }

    public string Connection { get; set; } = "USB";

    public string SyncRole { get; set; } = "Primary";

    public string DepthMode { get; set; } = "NFOV_UNBINNED";

    public int Fps { get; set; } = 15;
}
