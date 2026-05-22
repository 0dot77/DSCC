using System.Net;
using System.Net.Sockets;
using DSCC.Protocol;
using MessagePack;

namespace DSCC.Transport;

public sealed class UdpMessagePackSender : IStationSkeletonSender
{
    private readonly UdpClient client;
    private readonly IPEndPoint endpoint;
    private readonly MessagePackSerializerOptions serializerOptions;
    private bool disposed;

    public UdpMessagePackSender(
        string host,
        int port = ProtocolConstants.DefaultSkeletonPort,
        MessagePackSerializerOptions? serializerOptions = null)
    {
        endpoint = UdpEndpointFactory.Resolve(host, port);
        client = new UdpClient(endpoint.AddressFamily);
        this.serializerOptions = serializerOptions ?? MessagePackSerializerOptions.Standard;
    }

    public async ValueTask SendAsync(StationSkeletonFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(frame);

        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload = MessagePackSerializer.Serialize(frame, serializerOptions, cancellationToken);

        await client.SendAsync(payload, payload.Length, endpoint)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        client.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
