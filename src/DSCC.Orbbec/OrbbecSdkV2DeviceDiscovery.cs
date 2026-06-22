namespace DSCC.Orbbec;

public sealed class OrbbecSdkV2DeviceDiscovery : IOrbbecDeviceDiscovery
{
    private readonly OrbbecSdkRuntimeInfo runtimeInfo;
    private readonly OrbbecSdkV2Reflection sdk;

    public OrbbecSdkV2DeviceDiscovery(OrbbecSdkRuntimeInfo runtimeInfo)
    {
        this.runtimeInfo = runtimeInfo;
        sdk = new OrbbecSdkV2Reflection(runtimeInfo);
    }

    public Task<IReadOnlyList<OrbbecDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var devices = new List<OrbbecDeviceInfo>();
        object? context = null;
        object? deviceList = null;

        try
        {
            context = sdk.CreateContext();
            deviceList = sdk.Invoke(context, "QueryDeviceList");
            var count = Convert.ToUInt32(sdk.Invoke(deviceList!, "DeviceCount"));

            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                devices.Add(ReadDeviceInfo(deviceList!, index));
            }
        }
        finally
        {
            OrbbecSdkV2Reflection.DisposeIfPossible(deviceList);
            OrbbecSdkV2Reflection.DisposeIfPossible(context);
        }

        return Task.FromResult<IReadOnlyList<OrbbecDeviceInfo>>(devices);
    }

    public IOrbbecDevice CreateDevice(OrbbecDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);
        return new OrbbecSdkV2Device(deviceInfo, runtimeInfo);
    }

    private OrbbecDeviceInfo ReadDeviceInfo(object deviceList, uint index)
    {
        string serial = InvokeString(deviceList, "SerialNumber", index);
        string name = InvokeString(deviceList, "Name", index);
        string connection = InvokeString(deviceList, "ConnectionType", index);
        string? ipAddress = TryInvokeString(deviceList, "IPAddress", index);

        OrbbecDeviceInfo info = new(
            DetectDeviceType(name),
            serial,
            ParseConnection(connection),
            string.IsNullOrWhiteSpace(name) ? serial : name)
        {
            NativeName = name,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress,
            IsPlaceholder = false
        };

        object? device = null;
        object? nativeDeviceInfo = null;
        try
        {
            device = sdk.Invoke(deviceList, "GetDevice", index);
            nativeDeviceInfo = sdk.Invoke(device!, "GetDeviceInfo");
            info = info with
            {
                FirmwareVersion = TryInvokeString(nativeDeviceInfo!, "FirmwareVersion"),
                HardwareVersion = TryInvokeString(nativeDeviceInfo!, "HardwareVersion"),
                SupportedMinSdkVersion = TryInvokeString(nativeDeviceInfo!, "SupportedMinSdkVersion")
            };
        }
        catch
        {
            // DeviceInfo enrichment is best effort; discovery can still return serial/name/connection.
        }
        finally
        {
            OrbbecSdkV2Reflection.DisposeIfPossible(nativeDeviceInfo);
            OrbbecSdkV2Reflection.DisposeIfPossible(device);
        }

        return info;
    }

    private string InvokeString(object target, string methodName, params object?[] args)
    {
        return Convert.ToString(sdk.Invoke(target, methodName, args)) ?? string.Empty;
    }

    private string? TryInvokeString(object target, string methodName, params object?[] args)
    {
        try
        {
            return InvokeString(target, methodName, args);
        }
        catch
        {
            return null;
        }
    }

    private static OrbbecDeviceType DetectDeviceType(string name)
    {
        if (name.Contains("Bolt", StringComparison.OrdinalIgnoreCase))
        {
            return OrbbecDeviceType.FemtoBolt;
        }

        if (name.Contains("Mega", StringComparison.OrdinalIgnoreCase))
        {
            return OrbbecDeviceType.FemtoMega;
        }

        return OrbbecDeviceType.FemtoMega;
    }

    private static OrbbecConnectionKind ParseConnection(string connection)
    {
        if (connection.Contains("ethernet", StringComparison.OrdinalIgnoreCase) ||
            connection.Contains("net", StringComparison.OrdinalIgnoreCase) ||
            connection.Contains("ip", StringComparison.OrdinalIgnoreCase))
        {
            return OrbbecConnectionKind.Ethernet;
        }

        if (connection.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            return OrbbecConnectionKind.Usb;
        }

        return OrbbecConnectionKind.Unknown;
    }
}
