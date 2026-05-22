using DSCC.Core.Calibration;
using DSCC.Core.Configuration;
using DSCC.Core.Devices;
using DSCC.Core.Stations;

namespace DSCC.Core.Tests;

public sealed class DsccConfigStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), "dscc-core-tests", $"{Guid.NewGuid():N}.json");
        var store = new DsccConfigStore();
        var config = CreateConfig();

        try
        {
            store.Save(path, config);

            var loaded = store.Load(path);

            Assert.Equal("wall-a", loaded.WallId);
            Assert.Equal("127.0.0.1", loaded.Unity.Host);
            Assert.Equal(55010, loaded.Unity.SkeletonPort);
            Assert.True(loaded.Unity.MirrorSkeletonX);
            Assert.True(loaded.Unity.StabilizeHeadRotation);
            Assert.Equal(0.08, loaded.Unity.HeadRotationSmoothingHalfLifeSeconds);
            Assert.Equal(240.0, loaded.Unity.HeadRotationMaxDegreesPerSecond);
            Assert.Equal(0.45, loaded.Unity.HeadRotationMinConfidence);
            Assert.Equal(0.75, loaded.Unity.HeadRotationDeadZoneDegrees);
            var station = Assert.Single(loaded.Stations);
            Assert.Equal(1, station.StationId);
            Assert.True(station.Enabled);
            Assert.Equal("Station 1", station.DisplayName);
            Assert.Equal("FemtoBolt", station.Device.DeviceType);
            Assert.Equal("BOLT_SERIAL", station.Device.Serial);
            Assert.Equal(30, station.Device.Fps);
            Assert.Equal(new Vector3Meters(0.0, 0.0, 2.1), station.Calibration.FootMarkerCenter);
            Assert.Equal(-0.7, station.Calibration.TrackingRoi.MinX);
            Assert.Equal(2.8, station.Calibration.TrackingRoi.MaxZ);
            Assert.Equal(-3.0, station.Calibration.UnityAnchor.X);
            Assert.Equal(0.45, station.Thresholds.MinSkeletonConfidence);
            Assert.Equal(0.45, station.Thresholds.FootMarkerRadiusMeters);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static DsccConfig CreateConfig()
    {
        return new DsccConfig
        {
            WallId = "wall-a",
            Unity = new UnityLinkConfig
            {
                Host = "127.0.0.1",
                SkeletonPort = 55010,
                EventPort = 55011,
                StatusPort = 55012,
                MirrorSkeletonX = true,
                StabilizeHeadRotation = true,
                HeadRotationSmoothingHalfLifeSeconds = 0.08,
                HeadRotationMaxDegreesPerSecond = 240.0,
                HeadRotationMinConfidence = 0.45,
                HeadRotationDeadZoneDegrees = 0.75
            },
            Stations =
            [
                new StationConfig
                {
                    StationId = 1,
                    DisplayName = "Station 1",
                    Enabled = true,
                    Device = new DeviceProfile
                    {
                        DeviceType = "FemtoBolt",
                        Serial = "BOLT_SERIAL",
                        Connection = "USB",
                        SyncRole = "Primary",
                        DepthMode = "NFOV_UNBINNED",
                        Fps = 30
                    },
                    Calibration = new StationCalibration
                    {
                        FootMarkerCenter = new Vector3Meters(0.0, 0.0, 2.1),
                        TrackingRoi = new TrackingRoi
                        {
                            MinX = -0.7,
                            MaxX = 0.7,
                            MinY = 0.0,
                            MaxY = 2.4,
                            MinZ = 1.5,
                            MaxZ = 2.8
                        },
                        UnityAnchor = new UnityAnchor
                        {
                            X = -3.0,
                            Y = 0.0,
                            Z = 0.0,
                            RotationY = 0.0
                        }
                    },
                    Thresholds = new TrackingThresholds
                    {
                        EnterStableSeconds = 0.4,
                        LostGraceSeconds = 1.5,
                        ExitConfirmSeconds = 3.0,
                        MinSkeletonConfidence = 0.45,
                        FootMarkerRadiusMeters = 0.45
                    }
                }
            ]
        };
    }
}
