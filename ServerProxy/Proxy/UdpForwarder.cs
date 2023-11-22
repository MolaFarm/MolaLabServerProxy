using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServerProxy.Proxy;

internal class UdpForwarder
{
    private readonly ILogger<UdpForwarder> logger = App.AppLoggerFactory.CreateLogger<UdpForwarder>();
    private readonly UdpClient udpClientIPv4 = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly UdpClient udpClientIPv6 = new(new IPEndPoint(IPAddress.IPv6Loopback, 53));

    public async Task StartAsync()
    {
        while (!App.ServiceStatus.Equals(Status.Healthy)) await Task.Delay(1000);

        while (!App.ProxyTokenSource.IsCancellationRequested)
            try
            {
                var resultFromIpv6 = await udpClientIPv6.ReceiveAsync();
                _ = ProcessRequest(resultFromIpv6);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "UDP Package forward error");
            }
    }

    private async Task ProcessRequest(UdpReceiveResult resultFromIpv6)
    {
        try
        {
            await udpClientIPv4.SendAsync(resultFromIpv6.Buffer, resultFromIpv6.Buffer.Length,
                new IPEndPoint(IPAddress.Loopback, 53));
            logger.LogInformation($"Forwarded packet from {resultFromIpv6.RemoteEndPoint} to Loopback:53");

            var resultFromIpv4 = await udpClientIPv4.ReceiveAsync();

            await udpClientIPv6.SendAsync(resultFromIpv4.Buffer, resultFromIpv4.Buffer.Length,
                resultFromIpv6.RemoteEndPoint);

            logger.LogInformation($"Sent response back to {resultFromIpv6.RemoteEndPoint}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing request. ");
        }
    }
}