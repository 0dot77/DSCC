namespace DSCC.Orbbec;

public sealed class PlaceholderOrbbecDeviceDiscovery : IOrbbecDeviceDiscovery
{
    private readonly List<OrbbecDeviceInfo> placeholderDevices;

    public PlaceholderOrbbecDeviceDiscovery(IEnumerable<OrbbecDeviceInfo>? placeholderDevices = null)
    {
        this.placeholderDevices = placeholderDevices?.ToList() ?? OrbbecDeviceDiscovery.CreateDefaultPlaceholders().ToList();
    }

    public Task<IReadOnlyList<OrbbecDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<OrbbecDeviceInfo>>(placeholderDevices.ToArray());
    }

    public IOrbbecDevice CreateDevice(OrbbecDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        deviceInfo = deviceInfo with { IsPlaceholder = true };
        return deviceInfo.DeviceType switch
        {
            OrbbecDeviceType.FemtoBolt => new FemtoBoltDevice(deviceInfo),
            OrbbecDeviceType.FemtoMega => new FemtoMegaDevice(deviceInfo),
            _ => throw new NotSupportedException($"Unsupported Orbbec device type: {deviceInfo.DeviceType}.")
        };
    }

    public void SetPlaceholderDevices(IEnumerable<OrbbecDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        placeholderDevices.Clear();
        placeholderDevices.AddRange(devices.Select(device => device with { IsPlaceholder = true }));
    }
}
