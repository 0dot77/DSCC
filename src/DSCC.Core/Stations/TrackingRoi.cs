namespace DSCC.Core.Stations;

public sealed class TrackingRoi
{
    public double MinX { get; set; } = -0.7;

    public double MaxX { get; set; } = 0.7;

    public double MinY { get; set; } = 0.0;

    public double MaxY { get; set; } = 2.4;

    public double MinZ { get; set; } = 1.5;

    public double MaxZ { get; set; } = 2.8;

    public static TrackingRoi Default => new();

    public static TrackingRoi AroundFootMarker(
        Vector3Meters footMarkerCenter,
        double halfWidthMeters = 0.7,
        double depthBehindMeters = 1.0,
        double depthAheadMeters = 3.0,
        double minY = -1.2,
        double maxY = 1.2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halfWidthMeters);
        ArgumentOutOfRangeException.ThrowIfNegative(depthBehindMeters);
        ArgumentOutOfRangeException.ThrowIfNegative(depthAheadMeters);

        return new TrackingRoi
        {
            MinX = footMarkerCenter.X - halfWidthMeters,
            MaxX = footMarkerCenter.X + halfWidthMeters,
            MinY = minY,
            MaxY = maxY,
            MinZ = footMarkerCenter.Z - depthBehindMeters,
            MaxZ = footMarkerCenter.Z + depthAheadMeters
        };
    }

    public bool Contains(Vector3Meters point)
    {
        return point.X >= MinX
            && point.X <= MaxX
            && point.Y >= MinY
            && point.Y <= MaxY
            && point.Z >= MinZ
            && point.Z <= MaxZ;
    }
}
