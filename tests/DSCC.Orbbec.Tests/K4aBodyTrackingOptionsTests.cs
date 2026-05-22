using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class K4aBodyTrackingOptionsTests
{
    [Fact]
    public void Defaults_UseBoundedTrackingTimeouts()
    {
        var options = new K4aBodyTrackingOptions();

        Assert.True(options.CaptureTimeout >= TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.Zero, options.EnqueueTimeout);
        Assert.Equal(TimeSpan.Zero, options.ResultTimeout);
        Assert.Equal(OrbbecPreviewMode.Depth, options.PreviewMode);
        Assert.Equal(TimeSpan.Zero, options.PreviewInterval);
    }
}
