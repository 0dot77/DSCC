using DSCC.Protocol;

namespace DSCC.Replay;

public sealed class TrackingRoi
{
    public static TrackingRoi Default { get; } = new()
    {
        MinX = -0.7f,
        MaxX = 0.7f,
        MinY = 0.0f,
        MaxY = 2.4f,
        MinZ = 1.5f,
        MaxZ = 2.8f
    };

    public float MinX { get; set; }

    public float MaxX { get; set; }

    public float MinY { get; set; }

    public float MaxY { get; set; }

    public float MinZ { get; set; }

    public float MaxZ { get; set; }

    public bool Contains(Vector3Dto position)
    {
        return position.X >= MinX &&
            position.X <= MaxX &&
            position.Y >= MinY &&
            position.Y <= MaxY &&
            position.Z >= MinZ &&
            position.Z <= MaxZ;
    }

    public void Validate()
    {
        if (MinX >= MaxX)
        {
            throw new InvalidOperationException("Tracking ROI MinX must be less than MaxX.");
        }

        if (MinY >= MaxY)
        {
            throw new InvalidOperationException("Tracking ROI MinY must be less than MaxY.");
        }

        if (MinZ >= MaxZ)
        {
            throw new InvalidOperationException("Tracking ROI MinZ must be less than MaxZ.");
        }
    }
}
