using DSCC.Protocol;

namespace DSCC.Transport;

public interface IStationSkeletonSender : IDisposable, IAsyncDisposable
{
    ValueTask SendAsync(StationSkeletonFrame frame, CancellationToken cancellationToken = default);
}
