namespace DSCC.Orbbec;

public sealed record OrbbecDeviceConfiguration(
    OrbbecDeviceType DeviceType,
    string Serial,
    int? StationId,
    OrbbecConnectionKind Connection,
    OrbbecSyncRole SyncRole,
    OrbbecDepthMode DepthMode,
    int Fps,
    OrbbecPreviewMode PreviewMode = OrbbecPreviewMode.Depth,
    TimeSpan? PreviewInterval = null,
    bool EnableColorStream = false,
    bool EnableInfraredStream = false,
    bool EnableDepthStream = true,
    bool EnableFrameSync = true)
{
    public static OrbbecDeviceConfiguration ForDevice(OrbbecDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        return new OrbbecDeviceConfiguration(
            deviceInfo.DeviceType,
            deviceInfo.Serial,
            StationId: null,
            deviceInfo.Connection,
            OrbbecSyncRole.Primary,
            OrbbecDepthMode.NfovUnbinned,
            Fps: 15);
    }
}
