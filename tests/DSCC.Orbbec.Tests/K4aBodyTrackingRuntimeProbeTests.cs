using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class K4aBodyTrackingRuntimeProbeTests
{
    [Fact]
    public void Probe_WithEmptyDirectory_ReportsMissingRequirements()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dscc-k4a-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var runtime = K4aBodyTrackingRuntimeProbe.Probe(directory);

            Assert.False(runtime.IsAvailable);
            Assert.NotEmpty(runtime.MissingRequirements);
            Assert.Contains("K4A body tracking unavailable", runtime.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Probe_ExposesOfficialRuntimeVersions()
    {
        var runtime = K4aBodyTrackingRuntimeProbe.Probe(Path.GetTempPath());

        Assert.Equal("2.0.12", runtime.OfficialK4aWrapperVersion);
        Assert.Equal("1.1.2", runtime.OfficialBodyTrackingPackageVersion);
        Assert.Equal("1.4.1", runtime.OfficialSensorPackageVersion);
    }

    [Fact]
    public void Probe_InX64BodyTrackingBuild_FindsBundledRuntime()
    {
        if (!K4aBodyTrackingSkeletonSourceFactory.IsBuildEnabled)
        {
            return;
        }

        var runtime = K4aBodyTrackingRuntimeProbe.Probe();

        Assert.True(runtime.IsAvailable, runtime.Status);
        Assert.NotNull(runtime.OrbbecK4aWrapperPath);
        Assert.NotNull(runtime.NativeBodyTrackingLibraryPath);
        Assert.NotNull(runtime.BodyTrackingModelPath);
    }
}
