using DSCC.Protocol;

namespace DSCC.Orbbec;

public sealed class OrbbecSkeletonFrameArrivedEventArgs : EventArgs
{
    public required StationSkeletonFrame Frame { get; init; }

    public required int BodyCount { get; init; }

    public required int DepthWidth { get; init; }

    public required int DepthHeight { get; init; }

    public required double EstimatedFps { get; init; }

    public string? TrackingStatus { get; init; }

    public OrbbecPreviewMode PreviewMode { get; init; } = OrbbecPreviewMode.Depth;

    public DepthPreviewFrame? DepthPreview { get; init; }
}
