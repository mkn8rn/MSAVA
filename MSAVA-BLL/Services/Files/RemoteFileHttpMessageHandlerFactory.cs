using System.Net;
using System.Net.Sockets;

namespace MSAVA_BLL.Services.Files;

internal delegate ValueTask<Stream> RemoteFileConnectionFactory(
    IPEndPoint endPoint,
    CancellationToken cancellationToken);

public static class RemoteFileHttpMessageHandlerFactory
{
    public static HttpMessageHandler Create()
    {
        return Create(RemoteFileHostPolicy.ResolveHostAddressesAsync, ConnectSocketAsync);
    }

    internal static SocketsHttpHandler Create(
        HostAddressResolver resolver,
        RemoteFileConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                IPAddress address = await RemoteFileHostPolicy.ResolveConnectionAddressAsync(
                    context.DnsEndPoint.Host,
                    resolver,
                    "FileUrl",
                    cancellationToken);

                return await connectionFactory(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
            }
        };
    }

    private static async ValueTask<Stream> ConnectSocketAsync(
        IPEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
