namespace DSCC.Orbbec;

public sealed class OrbbecSdkV2Device : OrbbecDeviceBase
{
    private readonly OrbbecSdkV2Reflection sdk;
    private object? context;
    private object? device;
    private object? pipeline;
    private object? config;

    public OrbbecSdkV2Device(OrbbecDeviceInfo deviceInfo, OrbbecSdkRuntimeInfo runtimeInfo)
        : base(deviceInfo, OrbbecBackendMode.NativeSdkV2)
    {
        sdk = new OrbbecSdkV2Reflection(runtimeInfo);
    }

    public override Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != OrbbecDeviceStatus.Disconnected)
        {
            return Task.CompletedTask;
        }

        try
        {
            context = sdk.CreateContext();
            var deviceList = sdk.Invoke(context, "QueryDeviceList");
            try
            {
                device = string.IsNullOrWhiteSpace(DeviceInfo.Serial)
                    ? sdk.Invoke(deviceList!, "GetDevice", 0u)
                    : sdk.Invoke(deviceList!, "GetDeviceBySN", DeviceInfo.Serial);
            }
            finally
            {
                OrbbecSdkV2Reflection.DisposeIfPossible(deviceList);
            }

            ClearError();
            SetStatus(OrbbecDeviceStatus.Connected);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            throw;
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status == OrbbecDeviceStatus.Disconnected)
        {
            await ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Status == OrbbecDeviceStatus.Streaming)
        {
            return;
        }

        try
        {
            pipeline = sdk.CreatePipeline(device!);
            config = sdk.CreateConfig();

            if (Configuration.EnableDepthStream)
            {
                EnableVideoStream("OB_SENSOR_DEPTH", "OB_FORMAT_Y16");
            }

            if (Configuration.EnableColorStream)
            {
                EnableVideoStream("OB_SENSOR_COLOR", "OB_FORMAT_ANY");
            }

            if (Configuration.EnableFrameSync)
            {
                TryInvoke(pipeline, "EnableFrameSync");
            }

            sdk.Invoke(pipeline, "Start", config);
            ClearError();
            SetStatus(OrbbecDeviceStatus.Streaming);
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            throw;
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != OrbbecDeviceStatus.Streaming)
        {
            return Task.CompletedTask;
        }

        try
        {
            sdk.Invoke(pipeline!, "Stop");
            OrbbecSdkV2Reflection.DisposeIfPossible(config);
            OrbbecSdkV2Reflection.DisposeIfPossible(pipeline);
            config = null;
            pipeline = null;
            SetStatus(OrbbecDeviceStatus.Connected);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status == OrbbecDeviceStatus.Streaming)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        OrbbecSdkV2Reflection.DisposeIfPossible(device);
        OrbbecSdkV2Reflection.DisposeIfPossible(context);
        device = null;
        context = null;
        SetStatus(OrbbecDeviceStatus.Disconnected);
    }

    private void EnableVideoStream(string sensorTypeName, string formatName)
    {
        var sensorType = sdk.EnumValue("Orbbec.SensorType", sensorTypeName);
        var format = sdk.EnumValue("Orbbec.Format", formatName);
        sdk.Invoke(config!, "EnableVideoStream", sensorType, 0, 0, Configuration.Fps, format);
    }

    private void TryInvoke(object? target, string methodName)
    {
        if (target is null)
        {
            return;
        }

        try
        {
            sdk.Invoke(target, methodName);
        }
        catch
        {
            // Optional API. Older wrappers can still start streams without this.
        }
    }
}
