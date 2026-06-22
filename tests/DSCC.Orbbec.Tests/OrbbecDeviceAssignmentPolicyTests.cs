using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class OrbbecDeviceAssignmentPolicyTests
{
    [Theory]
    [InlineData("FemtoMega")]
    [InlineData("Femto Mega")]
    [InlineData("femto-mega")]
    [InlineData("mega")]
    public void IsCompatibleWithStationType_AllowsMegaAliasesForMegaDevice(string configuredDeviceType)
    {
        var device = CreateDevice(OrbbecDeviceType.FemtoMega);

        var compatible = OrbbecDeviceAssignmentPolicy.IsCompatibleWithStationType(device, configuredDeviceType);

        Assert.True(compatible);
    }

    [Fact]
    public void IsCompatibleWithStationType_RejectsBoltForMegaStation()
    {
        var device = CreateDevice(OrbbecDeviceType.FemtoBolt);

        var compatible = OrbbecDeviceAssignmentPolicy.IsCompatibleWithStationType(device, "FemtoMega");

        Assert.False(compatible);
    }

    [Fact]
    public void IsCompatibleWithStationType_AllowsEmptyStationTypeForLegacyConfigs()
    {
        var device = CreateDevice(OrbbecDeviceType.FemtoMega);

        var compatible = OrbbecDeviceAssignmentPolicy.IsCompatibleWithStationType(device, "");

        Assert.True(compatible);
    }

    [Fact]
    public void IsCompatibleWithStationType_RejectsUnknownConfiguredStationType()
    {
        var device = CreateDevice(OrbbecDeviceType.FemtoMega);

        var compatible = OrbbecDeviceAssignmentPolicy.IsCompatibleWithStationType(device, "UnknownCamera");

        Assert.False(compatible);
    }

    private static OrbbecDeviceInfo CreateDevice(OrbbecDeviceType deviceType)
    {
        return new OrbbecDeviceInfo(
            deviceType,
            $"{deviceType}-SERIAL",
            OrbbecConnectionKind.Usb,
            deviceType.ToString());
    }
}
