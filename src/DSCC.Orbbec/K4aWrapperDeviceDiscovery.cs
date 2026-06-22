#if DSCC_K4A_BODY_TRACKING
using SensorDevice = Microsoft.Azure.Kinect.Sensor.Device;
#endif

namespace DSCC.Orbbec;

public sealed class K4aWrapperDeviceDiscovery : IOrbbecDeviceDiscovery
{
#if DSCC_K4A_BODY_TRACKING
    private readonly K4aBodyTrackingRuntimeInfo runtimeInfo;

    public K4aWrapperDeviceDiscovery(K4aBodyTrackingRuntimeInfo runtimeInfo)
    {
        this.runtimeInfo = runtimeInfo;
    }

    public Task<IReadOnlyList<OrbbecDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!runtimeInfo.IsAvailable)
        {
            throw new InvalidOperationException(runtimeInfo.Status);
        }

        K4aWrapperNativeLoader.Configure(runtimeInfo);

        var devices = new List<OrbbecDeviceInfo>();
        var deviceCount = SensorDevice.GetInstalledCount();
        for (var index = 0; index < deviceCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SensorDevice? device = null;
            try
            {
                device = SensorDevice.Open(index);
                var serial = device.SerialNum;
                devices.Add(new OrbbecDeviceInfo(
                    OrbbecDeviceType.FemtoMega,
                    serial,
                    OrbbecConnectionKind.Unknown,
                    string.IsNullOrWhiteSpace(serial) ? $"K4A-compatible Orbbec #{index + 1}" : serial)
                {
                    NativeName = "K4A-compatible Orbbec",
                    IsPlaceholder = false
                });
            }
            catch
            {
                // A camera already claimed by another station can fail to open.
                // Discovery should still return any other cameras it can inspect.
            }
            finally
            {
                device?.Dispose();
            }
        }

        return Task.FromResult<IReadOnlyList<OrbbecDeviceInfo>>(devices);
    }

    public IOrbbecDevice CreateDevice(OrbbecDeviceInfo deviceInfo)
    {
        throw new NotSupportedException("K4A wrapper discovery is only used for Azure Kinect body tracking streams.");
    }
#else
    public K4aWrapperDeviceDiscovery(object runtimeInfo)
    {
    }

    public Task<IReadOnlyList<OrbbecDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("K4A wrapper discovery is only available in x64 body tracking builds.");
    }

    public IOrbbecDevice CreateDevice(OrbbecDeviceInfo deviceInfo)
    {
        throw new PlatformNotSupportedException("K4A wrapper discovery is only available in x64 body tracking builds.");
    }
#endif
}
