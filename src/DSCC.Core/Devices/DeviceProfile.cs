namespace DSCC.Core.Devices;

public sealed class DeviceProfile
{
    public string DeviceType { get; set; } = "FemtoBolt";

    public string Serial { get; set; } = string.Empty;

    public int? StationId { get; set; }

    public string Connection { get; set; } = "USB";

    public string SyncRole { get; set; } = "Primary";

    public string DepthMode { get; set; } = "WFOV_2X2BINNED";

    public int Fps { get; set; } = 15;
}
