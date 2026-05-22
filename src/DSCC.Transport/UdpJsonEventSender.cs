using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSCC.Protocol;

namespace DSCC.Transport;

public sealed class UdpJsonEventSender : IDisposable, IAsyncDisposable
{
    private readonly UdpClient client;
    private readonly IPEndPoint endpoint;
    private readonly JsonSerializerOptions jsonOptions;
    private bool disposed;

    public UdpJsonEventSender(
        string host,
        int port = ProtocolConstants.DefaultEventPort,
        JsonSerializerOptions? jsonOptions = null)
    {
        endpoint = UdpEndpointFactory.Resolve(host, port);
        client = new UdpClient(endpoint.AddressFamily);
        this.jsonOptions = jsonOptions ?? CreateDefaultJsonOptions();
    }

    public async ValueTask SendAsync(DsccEvent dsccEvent, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(dsccEvent);

        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(dsccEvent, jsonOptions);

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

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
