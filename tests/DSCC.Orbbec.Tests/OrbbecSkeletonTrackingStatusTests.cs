using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class OrbbecSkeletonTrackingStatusTests
{
    [Theory]
    [InlineData("initializing Cuda body tracker; preview only")]
    [InlineData("waiting for skeleton result")]
    [InlineData("tracker queue busy; dropping camera frame")]
    public void IsDepthOnlyTransient_ReturnsTrueForDepthOnlyStatuses(string trackingStatus)
    {
        Assert.True(OrbbecSkeletonTrackingStatus.IsDepthOnlyTransient(trackingStatus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no body")]
    [InlineData("tracking 1 body; selected 12")]
    public void IsDepthOnlyTransient_ReturnsFalseForSkeletonDeliveryStatuses(string? trackingStatus)
    {
        Assert.False(OrbbecSkeletonTrackingStatus.IsDepthOnlyTransient(trackingStatus));
    }

    [Fact]
    public void InitializingTrackerPreviewOnly_ProducesDepthOnlyStatus()
    {
        var trackingStatus = OrbbecSkeletonTrackingStatus.InitializingTrackerPreviewOnly("Cuda");

        Assert.Equal("initializing Cuda body tracker; preview only", trackingStatus);
        Assert.True(OrbbecSkeletonTrackingStatus.IsDepthOnlyTransient(trackingStatus));
    }
}
