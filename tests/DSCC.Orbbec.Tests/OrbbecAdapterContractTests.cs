using DSCC.Orbbec;

namespace DSCC.Orbbec.Tests;

public sealed class OrbbecAdapterContractTests
{
    [Fact]
    public async Task DiscoverAsync_InPlaceholderMode_ReturnsBoltAndMega()
    {
        var discovery = CreatePlaceholderDiscovery();

        var devices = await discovery.DiscoverAsync();

        Assert.Contains(devices, device =>
            device.DeviceType == OrbbecDeviceType.FemtoBolt &&
            device.Serial == "BOLT-PLACEHOLDER-001" &&
            device.Connection == OrbbecConnectionKind.Usb);

        Assert.Contains(devices, device =>
            device.DeviceType == OrbbecDeviceType.FemtoMega &&
            device.Serial == "MEGA-PLACEHOLDER-001" &&
            device.Connection == OrbbecConnectionKind.Ethernet);
    }

    [Theory]
    [InlineData(OrbbecDeviceType.FemtoBolt, OrbbecConnectionKind.Usb, typeof(FemtoBoltDevice))]
    [InlineData(OrbbecDeviceType.FemtoMega, OrbbecConnectionKind.Ethernet, typeof(FemtoMegaDevice))]
    public void CreateDevice_CreatesDeviceMatchingDeviceType(
        OrbbecDeviceType deviceType,
        OrbbecConnectionKind connection,
        Type expectedDeviceType)
    {
        var discovery = CreatePlaceholderDiscovery();
        var deviceInfo = new OrbbecDeviceInfo(
            deviceType,
            $"{deviceType}-SERIAL",
            connection,
            $"{deviceType} test device");

        var device = discovery.CreateDevice(deviceInfo);

        Assert.Equal(expectedDeviceType, device.GetType());
        Assert.Equal(deviceType, device.DeviceInfo.DeviceType);
        Assert.Equal(deviceInfo.Serial, device.DeviceInfo.Serial);
        Assert.Equal(connection, device.DeviceInfo.Connection);
        Assert.Equal(OrbbecDeviceStatus.Disconnected, device.Status);
    }

    [Fact]
    public async Task DeviceLifecycle_TransitionsThroughConnectStartStopDisconnect()
    {
        var device = new FemtoBoltDevice("BOLT-LIFECYCLE-001");

        Assert.Equal(OrbbecDeviceStatus.Disconnected, device.Status);

        await device.ConnectAsync();
        Assert.Equal(OrbbecDeviceStatus.Connected, device.Status);

        await device.StartAsync();
        Assert.Equal(OrbbecDeviceStatus.Streaming, device.Status);

        await device.StopAsync();
        Assert.Equal(OrbbecDeviceStatus.Connected, device.Status);

        await device.DisconnectAsync();
        Assert.Equal(OrbbecDeviceStatus.Disconnected, device.Status);
        Assert.Null(device.LastError);
    }

    [Fact]
    public void ApplyConfiguration_WithMismatchedSerial_FailsAndKeepsExistingConfiguration()
    {
        var device = new FemtoMegaDevice("MEGA-CONFIG-001", OrbbecConnectionKind.Ethernet);
        var originalConfiguration = device.Configuration;
        var mismatchedConfiguration = originalConfiguration with { Serial = "MEGA-CONFIG-OTHER" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => device.ApplyConfiguration(mismatchedConfiguration));

        Assert.Contains("serial", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalConfiguration, device.Configuration);
    }

    [Fact]
    public void ForDevice_DefaultsToMegaFieldTrackingDepthMode()
    {
        var deviceInfo = new OrbbecDeviceInfo(
            OrbbecDeviceType.FemtoMega,
            "MEGA-DEFAULT-001",
            OrbbecConnectionKind.Usb,
            "Femto Mega");

        var configuration = OrbbecDeviceConfiguration.ForDevice(deviceInfo);

        Assert.Equal(OrbbecDeviceType.FemtoMega, configuration.DeviceType);
        Assert.Equal(OrbbecDepthMode.NfovUnbinned, configuration.DepthMode);
        Assert.Equal(15, configuration.Fps);
    }

    [Fact]
    public void ProbeRuntime_WithBundledSdk_FindsManagedWrapperAndNativeLibrary()
    {
        var runtime = OrbbecSdkRuntimeProbe.Probe();

        Assert.True(runtime.IsManagedWrapperAvailable, runtime.ErrorMessage);
        Assert.True(runtime.IsNativeLibraryAvailable, runtime.ErrorMessage);
        Assert.NotNull(runtime.ManagedWrapperPath);
        Assert.NotNull(runtime.NativeLibraryPath);
    }

    [Fact]
    public async Task DiscoverAsync_InNativeSdkMode_UsesBundledSdkWithoutPlaceholderFallback()
    {
        var discovery = new OrbbecDeviceDiscovery(options: new OrbbecDeviceDiscoveryOptions
        {
            BackendMode = OrbbecBackendMode.NativeSdkV2,
            AllowPlaceholderFallback = false
        });

        var devices = await discovery.DiscoverAsync();

        Assert.DoesNotContain(devices, device => device.IsPlaceholder);
    }

    private static OrbbecDeviceDiscovery CreatePlaceholderDiscovery()
    {
        var discoveryType = typeof(OrbbecDeviceDiscovery);
        var optionsType = discoveryType.Assembly.GetType("DSCC.Orbbec.OrbbecDeviceDiscoveryOptions");
        if (optionsType is null)
        {
            return new OrbbecDeviceDiscovery();
        }

        var options = Activator.CreateInstance(optionsType)
            ?? throw new InvalidOperationException($"Could not create {optionsType.FullName}.");
        var backendModeProperty = optionsType.GetProperty("BackendMode");
        if (backendModeProperty?.PropertyType.IsEnum == true)
        {
            backendModeProperty.SetValue(
                options,
                Enum.Parse(backendModeProperty.PropertyType, "Placeholder"));
        }

        var constructor = discoveryType.GetConstructor(
            new[] { typeof(IEnumerable<OrbbecDeviceInfo>), optionsType });
        if (constructor is null)
        {
            return new OrbbecDeviceDiscovery();
        }

        return (OrbbecDeviceDiscovery)constructor.Invoke(new object?[] { null, options });
    }
}
