using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServerProxy.Proxy;

internal class IPv6Forwarder(IPAddress remoteIp, ushort remotePort, IPAddress localIp, ushort localPort)
{
    /// <summary>
    ///     Milliseconds
    /// </summary>
    private int ConnectionTimeout { get; } = 4 * 60 * 1000;

    public async Task StartAsync()
    {
        var logger = App.AppLoggerFactory.CreateLogger<IPv6Forwarder>();

        var connections = new ConcurrentDictionary<IPEndPoint, UdpConnection>();

        var remoteServerEndPoint = new IPEndPoint(remoteIp, remotePort);

        var localServer = new UdpClient(AddressFamily.InterNetworkV6);
        localServer.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        localServer.Client.Bind(new IPEndPoint(localIp, localPort));

        logger.LogInformation($"UDP proxy started [{localIp}]:{localPort} -> [{remoteIp}]:{remotePort}");

        _ = Task.Run(async () =>
        {
            while (!App.ProxyTokenSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                foreach (var connection in connections.ToArray())
                    if (connection.Value.LastActivity + ConnectionTimeout < Environment.TickCount64)
                    {
                        connections.TryRemove(connection.Key, out var c);
                        connection.Value.Stop();
                    }
            }
        });

        while (!App.ProxyTokenSource.IsCancellationRequested)
            try
            {
                var message = await localServer.ReceiveAsync().ConfigureAwait(false);
                var sourceEndPoint = message.RemoteEndPoint;
                var client = connections.GetOrAdd(sourceEndPoint,
                    ep =>
                    {
                        var udpConnection = new UdpConnection(localServer, sourceEndPoint, remoteServerEndPoint);
                        udpConnection.Run();
                        return udpConnection;
                    });
                await client.SendToServerAsync(message.Buffer).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "An exception occurred on receiving a client datagram");
            }
    }
}