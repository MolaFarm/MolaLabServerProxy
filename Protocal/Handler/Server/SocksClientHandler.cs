using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Protocal.Enum.Socks;
using Protocal.Handler.Base;
using Protocal.Request.Socks;

namespace Protocal.Handler.Server;

public class SocksClientHandler : BaseSocksClientHandler
{
    private readonly X509Certificate2 _certificate;
    private IPEndPoint _proxyEndPoint;

    public SocksClientHandler(X509Certificate2 cert, ILogger<SocksClientHandler> logger) : base(logger)
    {
        _certificate = cert;
    }

    public async Task HandleClientAsync(Socket client)
    {
        using (client)
        {
            try
            {
                var clientStream = new NetworkStream(client);
                var clientSslStream = new SslStream(clientStream, false);
                await clientSslStream.AuthenticateAsServerAsync(_certificate, false, SslProtocols.Tls13,
                    true);
                var buffer = new byte[8];
                await clientSslStream.ReadAsync(buffer, 0, 8);
                // Read the initial request from the client
                var handShakeRequest = HandShakeRequest.FromBytes(buffer);
                buffer = handShakeRequest.GetAcceptBytes(version, supportedAuthMethods);
                await clientSslStream.WriteAsync(buffer, 0, 2);

                buffer = new byte[258];
                await clientSslStream.ReadAsync(buffer, 0, 258);
                var proxyRequest = ProxyRequest.FromBytes(buffer);

                if (proxyRequest.DestinationAddressType.Equals(AddressType.Domain) &&
                    !proxyRequest.Hostname.EndsWith("labserver.internal"))
                {
                    _logger.LogWarning($"Proxy request from {client.RemoteEndPoint as IPEndPoint} => Reject");
                    client.Close();
                    return;
                }

                _logger.LogInformation($"Proxy request from {client.RemoteEndPoint as IPEndPoint} => Accept");

                switch (proxyRequest.Command)
                {
                    case ProxyCommand.Connect:
                        await HandleTcpConnect(proxyRequest, clientSslStream);
                        break;
                    case ProxyCommand.Bind:
                        // TODO
                        break;
                    case ProxyCommand.Udp:
                        await HandleUdpForward(proxyRequest, client, clientSslStream);
                        break;
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
}