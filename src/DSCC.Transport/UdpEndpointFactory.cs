using System.Net;
using System.Net.Sockets;

namespace DSCC.Transport;

internal static class UdpEndpointFactory
{
    public static IPEndPoint Resolve(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 0 and 65535.");
        }

        if (IPAddress.TryParse(host, out IPAddress? parsedAddress))
        {
            return new IPEndPoint(parsedAddress, port);
        }

        IPAddress? resolvedAddress = Dns.GetHostAddresses(host)
            .FirstOrDefault(address =>
                address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);

        return resolvedAddress is null
            ? throw new ArgumentException($"Could not resolve UDP host '{host}'.", nameof(host))
            : new IPEndPoint(resolvedAddress, port);
    }
}
