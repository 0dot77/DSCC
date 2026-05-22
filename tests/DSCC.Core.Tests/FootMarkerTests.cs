using DSCC.Core.Calibration;
using DSCC.Core.Stations;

namespace DSCC.Core.Tests;

public sealed class FootMarkerTests
{
    [Fact]
    public void IsInsideFootMarker_UsesHorizontalDistance()
    {
        var calibration = new StationCalibration
        {
            FootMarkerCenter = new Vector3Meters(0.0, 0.0, 2.1)
        };

        Assert.True(calibration.IsInsideFootMarker(new Vector3Meters(0.3, 1.0, 2.4), radiusMeters: 0.45));
        Assert.False(calibration.IsInsideFootMarker(new Vector3Meters(0.5, 0.0, 2.4), radiusMeters: 0.45));
    }
}
