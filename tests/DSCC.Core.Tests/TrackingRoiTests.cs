using DSCC.Core.Stations;

namespace DSCC.Core.Tests;

public sealed class TrackingRoiTests
{
    [Fact]
    public void Contains_ReturnsTrueForPointInsideBounds()
    {
        var roi = new TrackingRoi
        {
            MinX = -0.7,
            MaxX = 0.7,
            MinY = 0.0,
            MaxY = 2.4,
            MinZ = 1.5,
            MaxZ = 2.8
        };

        Assert.True(roi.Contains(new Vector3Meters(0.0, 1.1, 2.1)));
    }

    [Theory]
    [InlineData(-0.71, 1.1, 2.1)]
    [InlineData(0.71, 1.1, 2.1)]
    [InlineData(0.0, -0.01, 2.1)]
    [InlineData(0.0, 2.41, 2.1)]
    [InlineData(0.0, 1.1, 1.49)]
    [InlineData(0.0, 1.1, 2.81)]
    public void Contains_ReturnsFalseForPointOutsideBounds(double x, double y, double z)
    {
        var roi = TrackingRoi.Default;

        Assert.False(roi.Contains(new Vector3Meters(x, y, z)));
    }

    [Fact]
    public void AroundFootMarker_CreatesDepthRangeForDistanceTracking()
    {
        var roi = TrackingRoi.AroundFootMarker(new Vector3Meters(0.0, 0.0, 2.0));

        Assert.Equal(-0.7, roi.MinX);
        Assert.Equal(0.7, roi.MaxX);
        Assert.Equal(-1.2, roi.MinY);
        Assert.Equal(1.2, roi.MaxY);
        Assert.Equal(1.0, roi.MinZ);
        Assert.Equal(5.0, roi.MaxZ);
    }

    [Fact]
    public void AroundFootMarker_IncludesCameraSpacePelvisAboveSensorOrigin()
    {
        var roi = TrackingRoi.AroundFootMarker(new Vector3Meters(0.1, 0.0, 1.4));

        Assert.True(roi.Contains(new Vector3Meters(0.1, -0.13, 1.4)));
    }
}
