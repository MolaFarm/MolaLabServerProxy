using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Protocal.Enum.Socks;
using Protocal.Request.Socks;

namespace Protocal.Handler.Base;

public class BaseSocksClientHandler : BaseClientHandler
{
    protected static readonly ushort version = 5;
    protected static readonly List<AuthMethod> supportedAuthMethods = new() { AuthMethod.None };
    protected ILogger<BaseSocksClientHandler> _logger;

    public BaseSocksClientHandler(ILogger<BaseSocksClientHandler> logger)
    {
        _logger = logger;
    }

    public override async Task HandleClientAsync(Socket client)
    {
        using (client)
        {
            try
            {
                var clientStream = new NetworkStream(client);
                var buffer = new byte[8];
                await clientStream.ReadAsync(buffer, 0, 8);

                // Read the initial request from the client
                var handShakeRequest = HandShakeRequest.FromBytes(buffer);
                buffer = handShakeRequest.GetAcceptBytes(version, supportedAuthMethods);
                await clientStream.WriteAsync(buffer, 0, 2);

                buffer = new byte[258];
                await clientStream.ReadAsync(buffer, 0, 258);
                var proxyRequest = ProxyRequest.FromBytes(buffer);

                if (proxyRequest.DestinationAddressType.Equals(AddressType.Domain) &&
                    !proxyRequest.Hostname.EndsWith("labserver.internal"))
                {
                    _logger.LogWarning($"Proxy Request from {client.RemoteEndPoint as IPEndPoint} => Reject");
                    client.Close();
                    return;
                }

                _logger.LogInformation($"Proxy Request from {client.RemoteEndPoint as IPEndPoint} => Accept");

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

    protected async Task HandleTcpConnect(ProxyRequest request, Stream clientStream)
    {
        var remoteSocket = new Socket(request.DestinationAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        remoteSocket.NoDelay = true;
        await remoteSocket.ConnectAsync(new IPEndPoint(request.DestinationAddress, request.DestinationPort));
        var remoteStream = new NetworkStream(remoteSocket, true);

        // Accept Proxy Request
        await clientStream.WriteAsync(request.GetAcceptBytes(), 0, request.GetAcceptBytes().Length);

        await PipeStream(clientStream, remoteStream);
    }

    protected async Task HandleUdpForward(ProxyRequest request, Socket client, Stream clientStream)
    {
        try
        {
            var udpClient = new UdpClient();
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            udpClient.Client.Bind(endpoint);

            // Send the accept message to the client
            await clientStream.WriteAsync(request.GetAcceptBytes(client, endpoint.Port));

            // Receive data from the client
            var result = await udpClient.ReceiveAsync();
            var buffer = result.Buffer;

            // Extract address and port information from the received buffer
            var addressLength = buffer[3] == (byte)AddressType.IPv6 ? 16 : 4;
            var addressBytes = new byte[addressLength];
            Array.Copy(buffer, 4, addressBytes, 0, addressLength);
            var port = (buffer[addressLength + 4] << 8) | buffer[addressLength + 5];
            var remoteEndPoint = new IPEndPoint(new IPAddress(addressBytes), port);
            var data = new byte[buffer.Length - (addressLength + 6)];
            Array.Copy(buffer, addressLength + 6, data, 0, data.Length);

            // Send data to the remote server
            await udpClient.SendAsync(data, data.Length, remoteEndPoint);

            // Receive data from the remote server
            result = await udpClient.ReceiveAsync();
            buffer = result.Buffer;

            // Prepare and send the response to the client
            var clientEndpoint = client.RemoteEndPoint as IPEndPoint;
            addressBytes = clientEndpoint.Address.GetAddressBytes();
            addressLength = addressBytes.Length;
            data = new byte[addressLength + 5];
            data[0] = 0;
            data[1] = 0;
            data[2] = (byte)(clientEndpoint.AddressFamily == AddressFamily.InterNetwork
                ? AddressType.IPv4
                : AddressType.IPv6);
            Array.Copy(addressBytes, 0, data, 3, addressLength);
            data[addressLength + 3] = (byte)(port >> 8);
            data[addressLength + 4] = (byte)(port & 0xff);
            await udpClient.SendAsync(data, data.Length, clientEndpoint);

            // Dispose of the UDP client
            udpClient.Close();
        }
        catch (Exception ex)
        {
            // Handle exceptions
            _logger.LogWarning(ex, "Error in HandleUdpForward");
        }
    }
}