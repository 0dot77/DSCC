using DSCC.Core.Calibration;
using DSCC.Core.Devices;
using DSCC.Core.Stations;

namespace DSCC.Core.Configuration;

public sealed class DsccConfig
{
    public string WallId { get; set; } = string.Empty;

    public UnityLinkConfig Unity { get; set; } = new();

    public List<StationConfig> Stations { get; set; } = [];
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
