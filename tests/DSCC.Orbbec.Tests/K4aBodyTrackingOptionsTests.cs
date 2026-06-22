using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class K4aBodyTrackingOptionsTests
{
    [Fact]
    public void Defaults_UseBoundedTrackingTimeouts()
    {
        var options = new K4aBodyTrackingOptions();

        Assert.True(options.CaptureTimeout >= TimeSpan.FromMilliseconds(500));
        Assert.True(options.EnqueueTimeout >= TimeSpan.FromMilliseconds(500));
        Assert.True(options.ResultTimeout >= TimeSpan.FromMilliseconds(1000));
        Assert.Equal("Cuda", options.ProcessingMode);
        Assert.Equal("NFOV_UNBINNED", options.DepthMode);
        Assert.Equal(OrbbecPreviewMode.Depth, options.PreviewMode);
        Assert.Equal(TimeSpan.Zero, options.PreviewInterval);
    }
}
