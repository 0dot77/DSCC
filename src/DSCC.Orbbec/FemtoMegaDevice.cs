namespace DSCC.Orbbec;

public sealed class FemtoMegaDevice : OrbbecDeviceBase
{
    public FemtoMegaDevice(string serial, OrbbecConnectionKind connectionKind = OrbbecConnectionKind.Usb)
        : this(new OrbbecDeviceInfo(
            OrbbecDeviceType.FemtoMega,
            serial,
            connectionKind,
            "Femto Mega placeholder"))
    {
    }

    public FemtoMegaDevice(OrbbecDeviceInfo deviceInfo)
        : base(deviceInfo with { DeviceType = OrbbecDeviceType.FemtoMega })
    {
    }
}
