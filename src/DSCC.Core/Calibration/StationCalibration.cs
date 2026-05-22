using DSCC.Core.Stations;

namespace DSCC.Core.Calibration;

public sealed class StationCalibration
{
    public int? StationId { get; set; }

    public string CameraSerial { get; set; } = string.Empty;

    public Vector3Meters FootMarkerCenter { get; set; } = new(0.0, 0.0, 2.1);

    public TrackingRoi TrackingRoi { get; set; } = new();

    public UnityAnchor UnityAnchor { get; set; } = new();

    public bool IsInsideFootMarker(Vector3Meters footPosition, double radiusMeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusMeters);
        return FootMarkerCenter.HorizontalDistanceTo(footPosition) <= radiusMeters;
    }
}
