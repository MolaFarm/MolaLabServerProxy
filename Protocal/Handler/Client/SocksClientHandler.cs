using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Protocal.Enum.Socks;
using Protocal.Handler.Base;
using Protocal.Request.Socks;

namespace Protocal.Handler.Client;

public class SocksClientHandler : BaseSocksClientHandler
{
    private readonly IPEndPoint _proxyEndPoint;

    public SocksClientHandler(IPEndPoint proxyEndPoint, ILogger<SocksClientHandler> logger) : base(logger)
    {
        _proxyEndPoint = proxyEndPoint;
    }

    public async Task HandleClientAsync(Socket client, byte[] preReadRequest, int requestLength)
    {
        using (client)
        {
            try
            {
                var clientStream = new NetworkStream(client);

                // Read the initial request from the client
                var handShakeRequest = HandShakeRequest.FromBytes(preReadRequest);
                var buffer = handShakeRequest.GetAcceptBytes(version, supportedAuthMethods);
                await clientStream.WriteAsync(buffer, 0, 2);

                buffer = new byte[258];
                await clientStream.ReadAsync(buffer, 0, 258);
                var proxyRequest = ProxyRequest.FromBytes(buffer);

                if (proxyRequest.DestinationAddressType.Equals(AddressType.Domain) &&
                    proxyRequest.Hostname.EndsWith("labserver.internal"))
                {
                    _logger.LogInformation($"Connection to {proxyRequest.Hostname} => PROXY");
                    await ForwardToProxyServer(handShakeRequest, proxyRequest, client);
                }
                else
                {
                    _logger.LogInformation(
                        $"Connection to {(string.IsNullOrEmpty(proxyRequest.Hostname) ? proxyRequest.DestinationAddress : proxyRequest.Hostname)} => DIRECT");
                    switch (proxyRequest.Command)
                    {
                        case ProxyCommand.Connect:
                            await HandleTcpConnect(proxyRequest, clientStream);
                            break;
                        case ProxyCommand.Bind:
                            // TODO
                            break;
                        case ProxyCommand.Udp:
                            await HandleUdpForward(proxyRequest, client, clientStream);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle client");
            }
            finally
            {
                client.Close();
            }
        }
    }

    private async Task ForwardToProxyServer(HandShakeRequest handShakeRequest, ProxyRequest proxyRequest, Socket client)
    {
        using (var remoteClient = new TcpClient(_proxyEndPoint.Address.ToString(), _proxyEndPoint.Port))
        await using (var remoteStream = remoteClient.GetStream())
        await using (var remoteSslStream =
                     new SslStream(remoteStream, false, (sender, certificate, chain, errors) => true))
        await using (var clientStream = new NetworkStream(client))
        {
            await remoteSslStream.AuthenticateAsClientAsync("proxy.labserver.internal");
            await remoteSslStream.WriteAsync(handShakeRequest.ToBytes());
            var buffer = new byte[2];
            await remoteSslStream.ReadAsync(buffer, 0, 2);
            if (buffer[0] == handShakeRequest.Version)
                foreach (var authMethod in handShakeRequest.AuthMethods.Where(authMethod =>
                             (byte)authMethod == buffer[1]))
                {
                    await remoteSslStream.WriteAsync(proxyRequest.ToBytes());
                    await PipeStream(clientStream, remoteSslStream);
                }
        }
    }
}