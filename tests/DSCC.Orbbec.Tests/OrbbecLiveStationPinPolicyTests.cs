using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class OrbbecLiveStationPinPolicyTests
{
    [Fact]
    public void ValidateRequiredPins_PassesWhenEnabledStationsHaveConnectedCompatibleSerials()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [
                Pin(1, "MEGA-001"),
                Pin(2, "MEGA-002"),
                Pin(3, "MEGA-003"),
                Pin(4, "MEGA-004")
            ],
            [
                Mega("MEGA-001"),
                Mega("MEGA-002"),
                Mega("MEGA-003"),
                Mega("MEGA-004")
            ]);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateRequiredPins_IgnoresDisabledStations()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [
                Pin(1, "MEGA-001"),
                new OrbbecStationCameraPin(2, Enabled: false, CameraSerial: "", DeviceType: "FemtoMega")
            ],
            [Mega("MEGA-001")]);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateRequiredPins_RejectsMissingPinnedSerial()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [Pin(1, "")],
            [Mega("MEGA-001")]);

        Assert.Contains("station 1 has no pinned camera serial", failures);
    }

    [Fact]
    public void ValidateRequiredPins_RejectsDuplicateStationSerial()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [
                Pin(1, "MEGA-001"),
                Pin(2, "MEGA-001")
            ],
            [Mega("MEGA-001")]);

        Assert.Contains("stations 1 and 2 share camera serial MEGA-001", failures);
    }

    [Fact]
    public void ValidateRequiredPins_RejectsPinnedSerialThatIsNotConnected()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [Pin(1, "MEGA-001")],
            [Mega("MEGA-002")]);

        Assert.Contains("station 1 pinned camera serial MEGA-001 is not connected", failures);
    }

    [Fact]
    public void ValidateRequiredPins_RejectsIncompatibleDeviceType()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [Pin(1, "BOLT-001")],
            [Bolt("BOLT-001")]);

        Assert.Contains("station 1 expects FemtoMega, but BOLT-001 is FemtoBolt", failures);
    }

    [Fact]
    public void ValidateRequiredPins_RejectsDuplicateConnectedSerials()
    {
        var failures = OrbbecLiveStationPinPolicy.ValidateRequiredPins(
            [Pin(1, "MEGA-001")],
            [
                Mega("MEGA-001"),
                Mega("MEGA-001")
            ]);

        Assert.Contains("connected camera serial MEGA-001 appears more than once", failures);
    }

    private static OrbbecStationCameraPin Pin(int stationId, string serial)
    {
        return new OrbbecStationCameraPin(stationId, true, serial, "FemtoMega");
    }

    private static OrbbecDeviceInfo Mega(string serial)
    {
        return new OrbbecDeviceInfo(OrbbecDeviceType.FemtoMega, serial, OrbbecConnectionKind.Usb, serial);
    }

    private static OrbbecDeviceInfo Bolt(string serial)
    {
        return new OrbbecDeviceInfo(OrbbecDeviceType.FemtoBolt, serial, OrbbecConnectionKind.Usb, serial);
    }
}
