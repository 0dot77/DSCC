namespace DSCC.Orbbec;

public sealed class FemtoBoltDevice : OrbbecDeviceBase
{
    public FemtoBoltDevice(string serial)
        : this(new OrbbecDeviceInfo(
            OrbbecDeviceType.FemtoBolt,
            serial,
            OrbbecConnectionKind.Usb,
            "Femto Bolt placeholder"))
    {
    }

    public FemtoBoltDevice(OrbbecDeviceInfo deviceInfo)
        : base(deviceInfo with { DeviceType = OrbbecDeviceType.FemtoBolt })
    {
    }
}
