#pragma warning disable CA1416
using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Protocol.Enum.Socks;
using Protocol.Handler.Base;
using Protocol.Request.Http;
using Protocol.Request.Socks;

namespace Protocol.Handler.Client;

/// <summary>
///     Handles HTTP client requests and forwards them to a proxy server or processes them directly.
/// </summary>
public class HttpClientHandler : BaseHttpClientHandler
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HttpClientHandler" /> class.
    /// </summary>
    /// <param name="logger">Logger for logging events.</param>
    public HttpClientHandler(ILogger<HttpClientHandler> logger) : base(logger)
    {
    }

    /// <summary>
    ///     Handles the HTTP client asynchronously.
    /// </summary>
    /// <param name="client">The client socket.</param>
    /// <param name="preReadRequest">The pre-read request data.</param>
    /// <param name="requestLength">The length of the request.</param>
    /// <param name="connection">The QUIC connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleClientAsync(Socket client, byte[] preReadRequest, int requestLength,
        QuicConnection connection)
    {
        // Read the HTTP request from the client.
        try
        {
            var httpRequest = HttpRequest.FromBytes(preReadRequest, requestLength);
            if (httpRequest == null)
            {
                Logger.LogWarning("Null request received! Request will be ignored.");
                return;
            }

            // Handle request based on specific conditions.
            if (httpRequest.Host.EndsWith("labserver.internal") || httpRequest.Host.StartsWith("IP_ADDRESS_START_HERE"))
            {
                Logger.LogInformation($"Connection to {httpRequest.Host}:{httpRequest.Port} => PROXY");
                await ForwardToProxyServer(httpRequest, client, connection);
            }
            else
            {
                Logger.LogInformation($"Connection to {httpRequest.Host}:{httpRequest.Port} => DIRECT");
                await HandleHttpRequest(httpRequest, client);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to handle client");
        }
        finally
        {
            // Close the client connection.
            client.Close();
        }
    }

    /// <summary>
    ///     Forwards the HTTP request to a proxy server.
    /// </summary>
    /// <param name="httpRequest">The HTTP request to forward.</param>
    /// <param name="client">The client socket.</param>
    /// <param name="connection">The QUIC connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ForwardToProxyServer(HttpRequest httpRequest, Socket client, QuicConnection connection)
    {
        await using (var remoteStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional))
        await using (var clientStream = new NetworkStream(client))
        {
            // Handshake with the proxy server.
            var handshakeRequest = new HandShakeRequest
            {
                AuthMethods = new List<AuthMethod>
                {
                    AuthMethod.None
                },
                Version = 5
            };
            await remoteStream.WriteAsync(handshakeRequest.ToBytes());

            var buffer = new byte[2];
            await remoteStream.ReadAsync(buffer);

            // Check if the handshake was successful.
            if (!(buffer[0] == handshakeRequest.Version &&
                  handshakeRequest.AuthMethods.Contains((AuthMethod)buffer[1])))
                throw new InvalidCredentialException("Handshake failed");

            // Create a ProxyRequest based on the HTTP request.
            ProxyRequest? proxyRequest;
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

            // Send the ProxyRequest to the proxy server.
            await remoteStream.WriteAsync(proxyRequest.ToBytes());

            // Read the response from the proxy server.
            buffer = new byte[258];
            await remoteStream.ReadAsync(buffer);

            // Check if the proxy server accepted the request.
            if (!buffer[0].Equals(5) && !buffer[1].Equals(0))
                throw new InvalidDataException("Server rejected proxy request");

            // Pipe data between the client and the proxy server.
            await PipeStream(httpRequest, clientStream, remoteStream);
        }
    }
}