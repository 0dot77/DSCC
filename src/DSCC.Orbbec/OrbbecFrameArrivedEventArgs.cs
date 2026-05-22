namespace DSCC.Orbbec;

public sealed class OrbbecFrameArrivedEventArgs : EventArgs
{
    public required string Serial { get; init; }

    public required long FrameIndex { get; init; }

    public required long TimestampUsec { get; init; }

    public required int FrameCount { get; init; }

    public required bool HasDepth { get; init; }

    public int DepthWidth { get; init; }

    public int DepthHeight { get; init; }

    public required bool HasColor { get; init; }

    public int ColorWidth { get; init; }

    public int ColorHeight { get; init; }

    public required double EstimatedFps { get; init; }

    public OrbbecPreviewMode PreviewMode { get; init; } = OrbbecPreviewMode.Depth;

    public DepthPreviewFrame? DepthPreview { get; init; }
}
