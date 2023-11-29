#pragma warning disable CA1416
using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using Microsoft.Extensions.Logging;
using Protocol.Enum.Quic;
using Protocol.Enum.Socks;
using Protocol.Handler.Base;
using Protocol.Request.Quic;
using Protocol.Request.Socks;
using Protocol.Tools;

namespace Protocol.Handler.Server;

/// <summary>
///     Handles the connection for a QUIC server.
/// </summary>
public class QuicConnectionHandler : BaseSocksClientHandler
{
    private readonly CancellationToken _cancellationToken;
    private readonly QuicConnection _connection;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QuicConnectionHandler" /> class.
    /// </summary>
    /// <param name="connection">A QUIC connection.</param>
    /// <param name="ctsToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <param name="logger">A logger for logging events.</param>
    public QuicConnectionHandler(QuicConnection connection, CancellationToken ctsToken,
        ILogger<QuicConnectionHandler> logger) : base(logger)
    {
        _connection = connection;
        _cancellationToken = ctsToken;
    }

    /// <summary>
    ///     Handles the QUIC connection asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleConnectionAsync()
    {
        await using (_connection)
        {
            await using (var heartBeatStream = await _connection.AcceptInboundStreamAsync())
            {
                var heartBeatTask = Task.Run(async () => await HandleHeartBeatAsync(heartBeatStream));
                while (!heartBeatTask.IsCompleted)
                    try
                    {
                        var streamFromClientTask = _connection.AcceptInboundStreamAsync();
                        await Task.WhenAny(streamFromClientTask.AsTask(), heartBeatTask);
                        if (streamFromClientTask.IsCompleted)
                            _ = Task.Run(async () =>
                                await HandleClientAsync(streamFromClientTask.Result, _connection.RemoteEndPoint));
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, $"Lost connection to client: {_connection.RemoteEndPoint}");
                        break;
                    }

                Logger.LogWarning($"Lost connection to client: {_connection.RemoteEndPoint}");
            }
        }
    }

    /// <summary>
    ///     Handles the heartbeat for the QUIC connection asynchronously.
    /// </summary>
    /// <param name="heartBeatStream">The QUIC stream for heartbeat.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleHeartBeatAsync(QuicStream heartBeatStream)
    {
        await using (heartBeatStream)
        {
            var rto = new RTO(1000, 1);
            var rtt = 500.0;
            var currentRTO = 0;
            while (!_cancellationToken.IsCancellationRequested)
            {
                var timeoutCount = 0;

                // Get current RTO
                currentRTO = (int)rto.GetRTO(rtt, false);

                // Start a timer to measure RTO
                var timer = Stopwatch.StartNew();

                // Heart Beat from Client
                var tokenSource = new CancellationTokenSource();
                var bytes = new byte[3];
                var timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                var readTask = heartBeatStream.ReadAsync(bytes, 0, bytes.Length);
                await Task.WhenAny(timeoutTask, readTask);
                await tokenSource.CancelAsync();
                while (!readTask.IsCompleted)
                {
                    if (timeoutCount > 3) break;
                    timeoutCount++;
                    currentRTO =
                        (int)Math.Ceiling(rto.GetRTO(timeoutCount > 0 ? currentRTO * 2 : rtt, timeoutCount > 0));
                    tokenSource = new CancellationTokenSource();
                    timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                    await Task.WhenAny(timeoutTask, readTask);
                }

                if (readTask.IsFaulted || !readTask.IsCompleted) break;
                timeoutCount = 0;

                // Heart Beat to Client
                var heartBeatRequest = HeartBeat.FromBytes(bytes);
                if (!(heartBeatRequest.Version.Equals(Version) && heartBeatRequest.Actor.Equals(Actor.Client) &&
                      heartBeatRequest.IsAlive))
                    continue;
                ;
                tokenSource = new CancellationTokenSource();
                timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                var writeTask = heartBeatStream.WriteAsync(new HeartBeat
                    { Actor = Actor.Server, IsAlive = true, Version = Version }.ToBytes()).AsTask();
                await Task.WhenAny(timeoutTask, writeTask);
                await tokenSource.CancelAsync();
                while (!writeTask.IsCompleted)
                {
                    if (timeoutCount > 3) break;
                    timeoutCount++;
                    currentRTO =
                        (int)Math.Ceiling(rto.GetRTO(timeoutCount > 0 ? currentRTO * 2 : rtt, timeoutCount > 0));
                    tokenSource = new CancellationTokenSource();
                    timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                    await Task.WhenAny(timeoutTask, writeTask);
                }

                if (writeTask.IsFaulted || !writeTask.IsCompleted) break;

                // Stop the timer and get the RTT sample
                timer.Stop();
                rtt = timer.Elapsed.TotalMilliseconds;
                await Task.Delay(5000);
            }
        }
    }

    /// <summary>
    ///     Handles the client for the QUIC connection asynchronously.
    /// </summary>
    /// <param name="clientStream">The QUIC stream for the client.</param>
    /// <param name="remoteEndPoint">The remote endpoint of the client.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleClientAsync(QuicStream clientStream, IPEndPoint remoteEndPoint)
    {
        // Use the using statement to ensure proper disposal of resources.
        await using (clientStream)
        {
            try
            {
                // Read the initial bytes from the client.
                var buffer = new byte[8];
                await clientStream.ReadAsync(buffer, 0, 8);

                // Read the initial SOCKS handshake request from the client.
                var handShakeRequest = HandShakeRequest.FromBytes(buffer);

                // Create a response buffer based on the version and supported authentication methods.
                buffer = handShakeRequest.GetAcceptBytes(Version, SupportedAuthMethods);

                // Send the response to the client.
                await clientStream.WriteAsync(buffer, 0, 2);

                // Read the proxy request from the client.
                buffer = new byte[258];
                await clientStream.ReadAsync(buffer, 0, 258);
                var proxyRequest = ProxyRequest.FromBytes(buffer);

                // Check if the destination address type is a domain, and if the hostname is allowed.
                if (proxyRequest.DestinationAddressType.Equals(AddressType.Domain) &&
                    !proxyRequest.Hostname.EndsWith("labserver.internal"))
                {
                    // Log a warning and reject the proxy request.
                    Logger.LogWarning($"Proxy request from {remoteEndPoint} => Reject");
                    clientStream.Close();
                    return;
                }

                // Log information about the accepted proxy request.
                Logger.LogInformation($"Proxy request from {remoteEndPoint} => Accept");

                // Handle different types of proxy commands.
                switch (proxyRequest.Command)
                {
                    case ProxyCommand.Connect:
                        await HandleTcpConnect(proxyRequest, clientStream);
                        break;
                    case ProxyCommand.Bind:
                        // TODO: Implement handling for Bind command.
                        break;
                    case ProxyCommand.Udp:
                        // TODO: Implement handling for Udp command.
                        break;
                }
            }
            catch (Exception ex)
            {
                // Log an error if handling the client fails.
                Logger.LogError(ex, "Failed to handle client");
            }
            finally
            {
                // Close the client socket in the finally block to ensure proper cleanup.
                clientStream.Close();
            }
        }
    }
}