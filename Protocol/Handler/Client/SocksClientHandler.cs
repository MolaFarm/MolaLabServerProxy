#pragma warning disable CA1416
using System.Net.Quic;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Protocol.Enum.Socks;
using Protocol.Handler.Base;
using Protocol.Request.Socks;

namespace Protocol.Handler.Client;

/// <summary>
///     Represents a client handler for SOCKS protocol.
/// </summary>
public class SocksClientHandler : BaseSocksClientHandler
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SocksClientHandler" /> class.
    /// </summary>
    /// <param name="logger">The logger for logging.</param>
    public SocksClientHandler(ILogger<SocksClientHandler> logger) : base(logger)
    {
    }

    /// <summary>
    ///     Handles the client asynchronously based on the SOCKS protocol.
    /// </summary>
    /// <param name="client">The client socket.</param>
    /// <param name="preReadRequest">Pre-read request bytes.</param>
    /// <param name="requestLength">Length of the request.</param>
    /// <param name="connection">The QUIC connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleClientAsync(Socket client, byte[] preReadRequest, int requestLength,
        QuicConnection connection)
    {
        using (client)
        {
            try
            {
                var clientStream = new NetworkStream(client);

                // Read the initial request from the client
                var handShakeRequest = HandShakeRequest.FromBytes(preReadRequest);
                var buffer = handShakeRequest.GetAcceptBytes(Version, SupportedAuthMethods);
                await clientStream.WriteAsync(buffer, 0, 2);

                buffer = new byte[258];
                await clientStream.ReadAsync(buffer, 0, 258);
                var proxyRequest = ProxyRequest.FromBytes(buffer);

                if (proxyRequest.DestinationAddressType.Equals(AddressType.Domain) &&
                    proxyRequest.Hostname.EndsWith("labserver.internal"))
                {
                    Logger.LogInformation($"Connection to {proxyRequest.Hostname} => PROXY");
                    await ForwardToProxyServer(handShakeRequest, proxyRequest, client, connection);
                }
                else
                {
                    Logger.LogInformation(
                        $"Connection to {(string.IsNullOrEmpty(proxyRequest.Hostname) ? proxyRequest.DestinationAddress : proxyRequest.Hostname)} => DIRECT");
                    switch (proxyRequest.Command)
                    {
                        case ProxyCommand.Connect:
                            await HandleTcpConnect(proxyRequest, clientStream);
                            break;
                        case ProxyCommand.Bind:
                            // TODO: Handle Bind command
                            break;
                        case ProxyCommand.Udp:
                            await HandleUdpForward(proxyRequest, client, clientStream);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to handle client");
            }
            finally
            {
                client.Close();
            }
        }
    }

    /// <summary>
    ///     Forwards the connection to a proxy server asynchronously.
    /// </summary>
    /// <param name="handShakeRequest">The handshake request.</param>
    /// <param name="proxyRequest">The proxy request.</param>
    /// <param name="client">The client socket.</param>
    /// <param name="connection">The QUIC connection.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ForwardToProxyServer(HandShakeRequest handShakeRequest, ProxyRequest proxyRequest, Socket client,
        QuicConnection connection)
    {
        await using (var remoteStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional))
        await using (var clientStream = new NetworkStream(client))
        {
            await remoteStream.WriteAsync(handShakeRequest.ToBytes());
            var buffer = new byte[2];
            await remoteStream.ReadAsync(buffer, 0, 2);
            if (buffer[0] == handShakeRequest.Version)
                foreach (var authMethod in handShakeRequest.AuthMethods.Where(authMethod =>
                             (byte)authMethod == buffer[1]))
                {
                    await remoteStream.WriteAsync(proxyRequest.ToBytes());
                    await PipeStream(clientStream, remoteStream);
                }
        }
    }
}