namespace DSCC.Orbbec;

public interface IOrbbecDeviceDiscovery
{
    Task<IReadOnlyList<OrbbecDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default);

    IOrbbecDevice CreateDevice(OrbbecDeviceInfo deviceInfo);
}
