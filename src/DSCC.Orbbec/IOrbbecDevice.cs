namespace DSCC.Orbbec;

public interface IOrbbecDevice
{
    OrbbecDeviceInfo DeviceInfo { get; }

    OrbbecBackendMode BackendMode { get; }

    OrbbecDeviceConfiguration Configuration { get; }

    OrbbecDeviceStatus Status { get; }

    string? LastError { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
