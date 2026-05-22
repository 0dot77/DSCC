namespace DSCC.Core.Stations;

public sealed class SkeletonTrackingSample
{
    public bool HasSkeleton { get; set; } = true;

    public DateTimeOffset Timestamp { get; set; }

    public string CameraSerial { get; set; } = string.Empty;

    public Vector3Meters PelvisPosition { get; set; }

    public Vector3Meters? FootPosition { get; set; }

    public Vector3Meters? LeftFootPosition { get; set; }

    public Vector3Meters? RightFootPosition { get; set; }

    public double Confidence { get; set; }

    public static SkeletonTrackingSample Detected(
        Vector3Meters pelvisPosition,
        Vector3Meters footPosition,
        double confidence,
        DateTimeOffset timestamp)
    {
        return new SkeletonTrackingSample
        {
            HasSkeleton = true,
            Timestamp = timestamp,
            PelvisPosition = pelvisPosition,
            FootPosition = footPosition,
            Confidence = confidence
        };
    }

    public static SkeletonTrackingSample Lost(DateTimeOffset timestamp)
    {
        return new SkeletonTrackingSample
        {
            HasSkeleton = false,
            Timestamp = timestamp
        };
    }

    public IEnumerable<Vector3Meters> FootCandidates()
    {
        if (FootPosition is { } foot)
        {
            yield return foot;
        }

        if (LeftFootPosition is { } leftFoot)
        {
            yield return leftFoot;
        }

        if (RightFootPosition is { } rightFoot)
        {
            yield return rightFoot;
        }
    }
}
