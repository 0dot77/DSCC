namespace DSCC.Orbbec;

public sealed record OrbbecStationCameraPin(
    int StationId,
    bool Enabled,
    string? CameraSerial,
    string? DeviceType);
