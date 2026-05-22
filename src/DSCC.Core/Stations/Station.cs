using System.Text.Json.Serialization;
using DSCC.Core.Calibration;
using DSCC.Core.Devices;
using DSCC.Core.Diagnostics;

namespace DSCC.Core.Stations;

public sealed class Station
{
    public int StationId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public DeviceProfile Device { get; set; } = new();

    public StationCalibration Calibration { get; set; } = new();

    public TrackingThresholds Thresholds { get; set; } = new();

    public StationState State { get; set; } = StationState.Empty;

    public SkeletonTrackingSample? LastSkeletonFrame { get; set; }

    public StationDiagnostics Diagnostics { get; set; } = new();

    [JsonIgnore]
    public string AssignedCameraSerial
    {
        get => Device.Serial;
        set
        {
            Device.Serial = value;
            Calibration.CameraSerial = value;
        }
    }

    [JsonIgnore]
    public string DeviceType
    {
        get => Device.DeviceType;
        set => Device.DeviceType = value;
    }

    [JsonIgnore]
    public Vector3Meters FootMarkerCenter
    {
        get => Calibration.FootMarkerCenter;
        set => Calibration.FootMarkerCenter = value;
    }

    [JsonIgnore]
    public TrackingRoi TrackingRoi
    {
        get => Calibration.TrackingRoi;
        set => Calibration.TrackingRoi = value;
    }

    [JsonIgnore]
    public UnityAnchor UnityAnchor
    {
        get => Calibration.UnityAnchor;
        set => Calibration.UnityAnchor = value;
    }

    public bool IsInsideTrackingRoi(Vector3Meters pelvisPosition)
    {
        return Calibration.TrackingRoi.Contains(pelvisPosition);
    }

    public bool IsInsideFootMarker(Vector3Meters footPosition)
    {
        return Calibration.IsInsideFootMarker(footPosition, Thresholds.FootMarkerRadiusMeters);
    }
}
