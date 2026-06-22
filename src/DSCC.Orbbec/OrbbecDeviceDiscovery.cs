namespace DSCC.Orbbec;

public sealed class OrbbecDeviceDiscovery : IOrbbecDeviceDiscovery
{
    private readonly OrbbecDeviceDiscoveryOptions options;
    private readonly PlaceholderOrbbecDeviceDiscovery placeholderDiscovery;

    public OrbbecDeviceDiscovery(
        IEnumerable<OrbbecDeviceInfo>? placeholderDevices = null,
        OrbbecDeviceDiscoveryOptions? options = null)
    {
        this.options = options ?? new OrbbecDeviceDiscoveryOptions();
        placeholderDiscovery = new PlaceholderOrbbecDeviceDiscovery(placeholderDevices);
    }

    public Task<IReadOnlyList<OrbbecDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        return CreateBackendDiscovery().DiscoverAsync(cancellationToken);
    }

    public IOrbbecDevice CreateDevice(OrbbecDeviceInfo deviceInfo)
    {
        return CreateBackendDiscovery().CreateDevice(deviceInfo);
    }

    public void SetPlaceholderDevices(IEnumerable<OrbbecDeviceInfo> devices)
    {
        placeholderDiscovery.SetPlaceholderDevices(devices);
    }

    public static IReadOnlyList<OrbbecDeviceInfo> CreateDefaultPlaceholders()
    {
        return
        [
            new OrbbecDeviceInfo(
                OrbbecDeviceType.FemtoBolt,
                "BOLT-PLACEHOLDER-001",
                OrbbecConnectionKind.Usb,
                "Femto Bolt placeholder")
            {
                IsPlaceholder = true
            },
            new OrbbecDeviceInfo(
                OrbbecDeviceType.FemtoMega,
                "MEGA-PLACEHOLDER-001",
                OrbbecConnectionKind.Ethernet,
                "Femto Mega placeholder")
            {
                IsPlaceholder = true
            }
        ];
    }

    public OrbbecSdkRuntimeInfo ProbeRuntime()
    {
        return OrbbecSdkRuntimeProbe.Probe(options);
    }

    private IOrbbecDeviceDiscovery CreateBackendDiscovery()
    {
        var backendMode = options.BackendMode;
        if (backendMode == OrbbecBackendMode.Placeholder)
        {
            return placeholderDiscovery;
        }

        if (backendMode == OrbbecBackendMode.K4AWrapper)
        {
#if DSCC_K4A_BODY_TRACKING
            var runtime = K4aBodyTrackingRuntimeProbe.Probe();
            if (runtime.IsAvailable)
            {
                return new K4aWrapperDeviceDiscovery(runtime);
            }

            throw new InvalidOperationException(runtime.Status);
#else
            throw new PlatformNotSupportedException("K4A wrapper discovery is only available in x64 body tracking builds.");
#endif
        }

        if (backendMode is OrbbecBackendMode.Auto or OrbbecBackendMode.NativeSdkV2)
        {
            var runtime = OrbbecSdkRuntimeProbe.Probe(options);
            if (runtime.IsAvailable)
            {
                return new OrbbecSdkV2DeviceDiscovery(runtime);
            }

            if (backendMode == OrbbecBackendMode.NativeSdkV2 || !options.AllowPlaceholderFallback)
            {
                throw new OrbbecSdkUnavailableException(runtime);
            }
        }

        return placeholderDiscovery;
    }
}
