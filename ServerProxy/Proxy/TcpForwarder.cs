using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServerProxy.Proxy;

public class TcpForwarder
{
    private readonly TcpListener _listener = new(IPAddress.IPv6Loopback, 53);
    private ILogger<TcpForwarder> _logger;

    public async Task StartAsync()
    {
        _logger = App.AppLoggerFactory.CreateLogger<TcpForwarder>();
        _listener.Start();
        while (true)
            try
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = ProcessRequst(client);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TCP Connection forward error");
            }
    }

    private async Task ProcessRequst(TcpClient client)
    {
        using var forwardClient = new TcpClient();
        await forwardClient.ConnectAsync(IPAddress.Loopback, 53);
        await using var ServerStream = forwardClient.GetStream();
        await using var ClientStream = client.GetStream();
        var copyToServerTask = ClientStream.CopyToAsync(ServerStream);
        var copyToClientTask = ServerStream.CopyToAsync(ClientStream);
        _logger.LogInformation($"Forwarding Connection from {client.Client.RemoteEndPoint} to Loopback:53");
        await Task.WhenAll(copyToServerTask, copyToClientTask);
    }
}