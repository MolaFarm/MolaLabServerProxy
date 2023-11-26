using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Protocal.Enum.Socks;
using Protocal.Handler.Base;
using Protocal.Request.Http;
using Protocal.Request.Socks;

namespace Protocal.Handler.Client;

public class HttpClientHandler : BaseHttpClientHandler
{
    private readonly IPEndPoint _proxyEndPoint;

    public HttpClientHandler(IPEndPoint proxyEndPoint, ILogger<HttpClientHandler> logger) : base(logger)
    {
        _proxyEndPoint = proxyEndPoint;
    }

    public async Task HandleClientAsync(Socket client, byte[] preReadRequest, int requestLength)
    {
        // Read the HTTP request from the client
        try
        {
            var httpRequest = HttpRequest.FromBytes(preReadRequest, requestLength);
            if (httpRequest == null)
            {
                _logger.LogWarning("Null request received! Request will be ignore.");
                return;
            }

            // Handle request
            if (httpRequest.Host.EndsWith("labserver.internal") || httpRequest.Host.StartsWith("IP_ADDRESS_START_HERE"))
            {
                _logger.LogInformation($"Connection to {httpRequest.Host}:{httpRequest.Port} => PROXY");
                await ForwardToProxyServer(httpRequest, client);
            }
            else
            {
                _logger.LogInformation($"Connection to {httpRequest.Host}:{httpRequest.Port} => DIRECT");
                await HandleHttpRequest(httpRequest, client);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle client");
        }
        finally
        {
            // Close the client connection
            client.Close();
        }
    }

    private async Task ForwardToProxyServer(HttpRequest httpRequest, Socket client)
    {
        using (var remoteClient = new TcpClient(_proxyEndPoint.Address.ToString(), _proxyEndPoint.Port))
        await using (var remoteStream = remoteClient.GetStream())
        await using (var remoteSslStream =
                     new SslStream(remoteStream, false, (sender, certificate, chain, errors) => true))
        await using (var clientStream = new NetworkStream(client))
        {
            await remoteSslStream.AuthenticateAsClientAsync("proxy.labserver.internal");
            // HandShake
            var handshakeRequest = new HandShakeRequest
            {
                AuthMethods = new List<AuthMethod>
                {
                    AuthMethod.None
                },
                Version = 5
            };
            await remoteSslStream.WriteAsync(handshakeRequest.ToBytes());
            var buffer = new byte[2];
            var bytesRead = await remoteSslStream.ReadAsync(buffer);
            if (!(buffer[0] == handshakeRequest.Version &&
                  handshakeRequest.AuthMethods.Contains((AuthMethod)buffer[1])))
                throw new InvalidCredentialException("Handshake failed");
            ProxyRequest? proxyRequest = null;
            if (httpRequest.Host.StartsWith("IP_ADDRESS_START_HERE"))
                proxyRequest = new ProxyRequest
                {
                    Command = ProxyCommand.Connect,
                    DestinationAddressType = AddressType.IPv4,
                    DestinationAddress = IPAddress.Parse(httpRequest.Host),
                    DestinationPort = (ushort)httpRequest.Port,
                    ReverseData = 0,
                    Version = 5
                };
            else
                proxyRequest = new ProxyRequest
                {
                    Command = ProxyCommand.Connect,
                    DestinationAddressType = AddressType.Domain,
                    DestinationPort = (ushort)httpRequest.Port,
                    Hostname = httpRequest.Host,
                    ReverseData = 0,
                    Version = 5
                };
            await remoteSslStream.WriteAsync(proxyRequest.ToBytes());
            buffer = new byte[258];
            bytesRead = await remoteSslStream.ReadAsync(buffer);
            if (!buffer[0].Equals(5) && !buffer[1].Equals(0))
                throw new InvalidDataException("Server rejected proxy request");

            await PipeStream(httpRequest, clientStream, remoteSslStream);
        }
    }
}