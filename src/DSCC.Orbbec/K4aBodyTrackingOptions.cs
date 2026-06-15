namespace DSCC.Orbbec;

public sealed class K4aBodyTrackingOptions
{
    public int StationId { get; init; }

    public string CameraSerial { get; init; } = string.Empty;

    public string DeviceType { get; init; } = "FemtoBolt";

    public int Fps { get; init; } = 15;

    public string DepthMode { get; init; } = "WFOV_2X2BINNED";

    public string ProcessingMode { get; init; } = "Cpu";

    /// <summary>
    /// Absolute path to the k4abt ONNX model (e.g. the lite model). Empty keeps
    /// the tracker default (full model).
    /// </summary>
    public string ModelPath { get; init; } = string.Empty;

    public int GpuDeviceId { get; init; }

    /// <summary>
    /// Station ROI in depth-camera space; used to keep body selection on the
    /// person standing inside the station when multiple people are visible.
    /// </summary>
    public BodySelectionRoi? BodySelectionRoi { get; init; }

    public string SensorOrientation { get; init; } = "Default";

    public OrbbecPreviewMode PreviewMode { get; init; } = OrbbecPreviewMode.Depth;

    public TimeSpan PreviewInterval { get; init; } = TimeSpan.Zero;

    public TimeSpan CaptureTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan EnqueueTimeout { get; init; } = TimeSpan.Zero;

    public TimeSpan ResultTimeout { get; init; } = TimeSpan.Zero;
}
