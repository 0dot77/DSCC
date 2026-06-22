namespace DSCC.Orbbec;

public interface IOrbbecSkeletonFrameSource : IAsyncDisposable
{
    event EventHandler<OrbbecSkeletonFrameArrivedEventArgs>? FrameArrived;

    event EventHandler<string>? SourceStatus;

    event EventHandler<string>? SourceError;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
