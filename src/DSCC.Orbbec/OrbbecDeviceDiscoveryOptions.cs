namespace DSCC.Orbbec;

public sealed class OrbbecDeviceDiscoveryOptions
{
    public OrbbecBackendMode BackendMode { get; set; } = OrbbecBackendMode.Auto;

    public bool AllowPlaceholderFallback { get; set; } = true;

    public string? ManagedWrapperAssemblyPath { get; set; }

    public string? NativeLibraryDirectory { get; set; }

    public bool EnableNetworkDeviceEnumeration { get; set; }
}
