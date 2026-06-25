using DSCC.Core.Calibration;
using DSCC.Core.Devices;
using DSCC.Core.Stations;

namespace DSCC.Core.Configuration;

public sealed class DsccConfig
{
    public string WallId { get; set; } = string.Empty;

    /// <summary>
    /// When true, live start fills empty stations with unassigned discovered
    /// devices. Off by default: stations use only the serials explicitly
    /// pinned to them (UI device list / Auto assign button / config).
    /// </summary>
    public bool AutoAssignDevicesOnStart { get; set; }

    public UnityLinkConfig Unity { get; set; } = new();

    public BodyTrackingConfig BodyTracking { get; set; } = new();

    public List<StationConfig> Stations { get; set; } = [];
}

public sealed class BodyTrackingConfig
{
    /// <summary>
    /// Tracker processing mode used per station. CUDA can be faster on a
    /// matched NVIDIA stack, but DirectML is the safer Windows field default.
    /// </summary>
    public List<string> ProcessingModes { get; set; } = ["DirectML"];

    /// <summary>
    /// Run K4ABT in the native tracker sidecar instead of loading k4abt.dll
    /// into the WPF process. This keeps native crashes from taking down the UI.
    /// </summary>
    public bool UseTrackerSidecar { get; set; } = true;

    /// <summary>
    /// Optional absolute or relative path to dscc-k4abt-tracker.exe. Empty
    /// resolves the repository artifact path.
    /// </summary>
    public string TrackerExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Use the k4abt lite model. Strongly recommended when several trackers
    /// share one GPU; the full model is heavier per instance.
    /// </summary>
    public bool UseLiteModel { get; set; } = true;

    /// <summary>
    /// Upper bound applied to each station's configured camera fps for the
    /// body tracking pipeline.
    /// </summary>
    public int MaxFps { get; set; } = 15;

    public int GpuDeviceId { get; set; }

    /// <summary>
    /// Delay after starting one tracker sidecar before starting the next station.
    /// Some multi-Femto Mega rigs reject simultaneous USB control transfers.
    /// </summary>
    public double TrackerStartupDelayMilliseconds { get; set; }

    /// <summary>
    /// Minimum interval between depth/IR preview images. 0 generates a preview
    /// for every frame.
    /// </summary>
    public double PreviewIntervalMilliseconds { get; set; } = 150.0;
}

public sealed class UnityLinkConfig
{
    public string Host { get; set; } = "127.0.0.1";

    public int SkeletonPort { get; set; } = 55010;

    public int EventPort { get; set; } = 55011;

    public int StatusPort { get; set; } = 55012;

    public bool MirrorSkeletonX { get; set; } = true;

    public bool StabilizeHeadRotation { get; set; } = true;

    public double HeadRotationSmoothingHalfLifeSeconds { get; set; } = 0.08;

    public double HeadRotationMaxDegreesPerSecond { get; set; } = 240.0;

    public double HeadRotationMinConfidence { get; set; } = 0.45;

    public double HeadRotationDeadZoneDegrees { get; set; } = 0.75;
}

public sealed class StationConfig
{
    public int StationId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public DeviceProfile Device { get; set; } = new();

    public StationCalibration Calibration { get; set; } = new();

    public TrackingThresholds Thresholds { get; set; } = new();

    public Station ToStation()
    {
        return new Station
        {
            StationId = StationId,
            DisplayName = DisplayName,
            Enabled = Enabled,
            Device = Device,
            Calibration = Calibration,
            Thresholds = Thresholds
        };
    }

    public static StationConfig FromStation(Station station)
    {
        ArgumentNullException.ThrowIfNull(station);

        return new StationConfig
        {
            StationId = station.StationId,
            DisplayName = station.DisplayName,
            Enabled = station.Enabled,
            Device = station.Device,
            Calibration = station.Calibration,
            Thresholds = station.Thresholds
        };
    }
}
