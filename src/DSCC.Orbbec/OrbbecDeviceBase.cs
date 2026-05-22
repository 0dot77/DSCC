namespace DSCC.Orbbec;

public abstract class OrbbecDeviceBase : IOrbbecDevice
{
    protected OrbbecDeviceBase(OrbbecDeviceInfo deviceInfo)
        : this(deviceInfo, OrbbecBackendMode.Placeholder)
    {
    }

    protected OrbbecDeviceBase(OrbbecDeviceInfo deviceInfo, OrbbecBackendMode backendMode)
    {
        DeviceInfo = deviceInfo;
        BackendMode = backendMode;
        Configuration = OrbbecDeviceConfiguration.ForDevice(deviceInfo);
    }

    public OrbbecDeviceInfo DeviceInfo { get; }

    public OrbbecBackendMode BackendMode { get; }

    public OrbbecDeviceConfiguration Configuration { get; private set; }

    public OrbbecDeviceStatus Status { get; private set; } = OrbbecDeviceStatus.Disconnected;

    public string? LastError { get; private set; }

    public virtual Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastError = null;
        Status = OrbbecDeviceStatus.Connected;
        return Task.CompletedTask;
    }

    public virtual Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status is OrbbecDeviceStatus.Disconnected)
        {
            throw new InvalidOperationException("Connect the Orbbec device before starting the stream.");
        }

        LastError = null;
        Status = OrbbecDeviceStatus.Streaming;
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status is OrbbecDeviceStatus.Streaming)
        {
            Status = OrbbecDeviceStatus.Connected;
        }

        return Task.CompletedTask;
    }

    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Status = OrbbecDeviceStatus.Disconnected;
        return Task.CompletedTask;
    }

    public void ApplyConfiguration(OrbbecDeviceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.Equals(DeviceInfo.Serial, configuration.Serial, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Configuration serial must match the connected device serial.");
        }

        Configuration = configuration;
    }

    protected void SetStatus(OrbbecDeviceStatus status)
    {
        Status = status;
    }

    protected void ClearError()
    {
        LastError = null;
    }

    protected void SetError(string message)
    {
        LastError = message;
        Status = OrbbecDeviceStatus.Error;
    }
}
