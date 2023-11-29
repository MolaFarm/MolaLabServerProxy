#pragma warning disable CA1416
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Protocol.Enum.Quic;
using Protocol.Handler.Client;
using Protocol.Request.Quic;
using Protocol.Tools;
using ServerProxy.Tools;

namespace ServerProxy.Proxy;

/// <summary>
///     Represents a mix protocol server for handling different proxy protocols.
/// </summary>
internal class MixProtocolServer
{
    private readonly ILogger<MixProtocolServer> _logger;
    private readonly int _serverPort;
    private QuicConnection _connection;
    private QuicClientConnectionOptions _connectionOptions;
    private HttpClientHandler _httpClientHandler;
    private readonly int _listenPort;
    private SocksClientHandler _socksClientHandler;

    /// <summary>
    ///     Initializes a new instance of the MixProtocolServer class.
    /// </summary>
    /// <param name="serverPort">The port of the server to connect to.</param>
    /// <param name="listenPort">The port on which the server should listen for incoming connections.</param>
    public MixProtocolServer(int serverPort, int listenPort)
    {
        _serverPort = serverPort;
        _listenPort = listenPort;
        _logger = App.AppLoggerFactory.CreateLogger<MixProtocolServer>();
    }

    /// <summary>
    ///     Asynchronously starts the mix protocol server.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync()
    {
        var preferIp = await RouteHelper.GetAvailableIP();
        _connectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(preferIp, _serverPort),
            DefaultStreamErrorCode = 0x0A,
            DefaultCloseErrorCode = 0x0B,
            MaxInboundUnidirectionalStreams = 10,
            MaxInboundBidirectionalStreams = 100,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            }
        };

        while (!App.ProxyTokenSource.IsCancellationRequested)
            try
            {
                _connection = await QuicConnection.ConnectAsync(_connectionOptions);
                _logger.LogInformation($"Connected {_connection.LocalEndPoint} --> {_connection.RemoteEndPoint}");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to server, will try again after 5s.");
                await Task.Delay(5000, App.ProxyTokenSource.Token);
            }

        _httpClientHandler =
            new HttpClientHandler(App.AppLoggerFactory.CreateLogger<HttpClientHandler>());
        _socksClientHandler =
            new SocksClientHandler(App.AppLoggerFactory.CreateLogger<SocksClientHandler>());
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, _listenPort));
        listener.Listen(10);
        _logger.LogInformation($"Server now listening at {listener.LocalEndPoint as IPEndPoint}");

        var heartBeatStream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        _ = Task.Run(async () => await HealthChecker(heartBeatStream));

        Notification.Show("代理服务", "代理服务已启动");

        while (!App.ProxyTokenSource.IsCancellationRequested)
            try
            {
                var client = await listener.AcceptAsync();
                _ = Task.Run(() => DetectProtocalAndProxy(client));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle client");
            }
    }

    /// <summary>
    ///     Asynchronously detects the protocol and handles the proxy for the incoming client.
    /// </summary>
    /// <param name="client">The incoming client socket.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DetectProtocalAndProxy(Socket client)
    {
        var stream = new NetworkStream(client);
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer, 0, 4096);
        if (buffer[0] == 0x05 && buffer[1] == (byte)(bytesRead - 2))
            await _socksClientHandler.HandleClientAsync(client, buffer, bytesRead, _connection);
        else
            await _httpClientHandler.HandleClientAsync(client, buffer, bytesRead, _connection);
    }

    /// <summary>
    ///     Asynchronously performs health checking for the proxy server.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HealthChecker(QuicStream stream)
    {
        var rto = new RTO(1000, 1);
        var rtt = 500.0;
        var highRttWarning = false;
        var currentRTO = 0;
        var lastStatus = Status.Starting;
        var currentStatus = Status.Starting;

        await using (stream)
        {
            while (!App.ProxyTokenSource.IsCancellationRequested)
                try
                {
                    var timeoutCount = 0;

                    // Get current RTO
                    currentRTO = (int)Math.Ceiling(rto.GetRTO(rtt, false));
                    if (rtt > 1000 && !highRttWarning)
                        Notification.Show("检测到高连接延迟", $"当前数据往返时间：{rtt}ms\n保守预测下次往返时间：{currentRTO}ms");
                    if (rtt < 1000 && highRttWarning) highRttWarning = false;

                    // Start a timer to measure RTO
                    var timer = Stopwatch.StartNew();

                    // Send Heart Beat to Server
                    var heartBeatRequest = new HeartBeat
                    {
                        Actor = Actor.Client,
                        IsAlive = true,
                        Version = 5
                    };
                    var tokenSource = new CancellationTokenSource();
                    var timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                    var writeTask = stream.WriteAsync(heartBeatRequest.ToBytes()).AsTask();
                    await Task.WhenAny(timeoutTask, writeTask);
                    tokenSource.Cancel();
                    while (!writeTask.IsCompleted)
                    {
                        if (timeoutCount > 3) throw new TimeoutException();
                        timeoutCount++;
                        currentRTO =
                            (int)Math.Ceiling(rto.GetRTO(timeoutCount > 0 ? currentRTO * 2 : rtt, timeoutCount > 0));
                        tokenSource = new CancellationTokenSource();
                        timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                        await Task.WhenAny(timeoutTask, writeTask);
                    }

                    if (writeTask.IsFaulted || !writeTask.IsCompleted) throw new TimeoutException();

                    // Read Heart Beat to Server
                    var bytes = new byte[3];
                    tokenSource = new CancellationTokenSource();
                    timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                    var readTask = stream.ReadAsync(bytes, 0, 3);
                    await Task.WhenAny(timeoutTask, readTask);
                    tokenSource.Cancel();
                    while (!readTask.IsCompleted)
                    {
                        if (timeoutCount > 3) throw new TimeoutException();
                        timeoutCount++;
                        currentRTO =
                            (int)Math.Ceiling(rto.GetRTO(timeoutCount > 0 ? currentRTO * 2 : rtt, timeoutCount > 0));
                        tokenSource = new CancellationTokenSource();
                        timeoutTask = Task.Delay(currentRTO, tokenSource.Token);
                        await Task.WhenAny(timeoutTask, readTask);
                    }

                    if (readTask.IsFaulted || !readTask.IsCompleted) throw new TimeoutException();
                    heartBeatRequest = HeartBeat.FromBytes(bytes);
                    if (!(heartBeatRequest.Version.Equals(5) && heartBeatRequest.Actor.Equals(Actor.Server) &&
                          heartBeatRequest.IsAlive))
                        continue;

                    // Stop the timer and get the RTT sample
                    timer.Stop();
                    rtt = timer.Elapsed.TotalMilliseconds;
                    timeoutCount = 0;
                    currentStatus = Status.Healthy;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "UnHealthy connection detected!");
                    currentStatus = Status.UnHealthy;

                    // Try to recreate connection
                    await _connection.DisposeAsync();
                    try
                    {
                        _connection = await QuicConnection.ConnectAsync(_connectionOptions);
                        rto = new RTO(1000, 1);
                        _logger.LogInformation(
                            $"Connected {_connection.LocalEndPoint} --> {_connection.RemoteEndPoint}");
                        stream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Failed to connect to server, will try again later");
                    }
                }
                finally
                {
                    if (lastStatus != currentStatus)
                    {
                        _logger.LogInformation($"Proxy status: {lastStatus} => {currentStatus}");
                        lastStatus = currentStatus;
                        Dispatcher.UIThread.Invoke(() => (Application.Current as App)?.SetStatus(currentStatus));

                        switch (currentStatus)
                        {
                            case Status.Healthy:
                                Notification.Show("提示", "代理服务状态: 健康");
                                break;
                            case Status.UnHealthy:
                                Notification.Show("警告", "代理服务状态: 连接阻塞");
                                break;
                        }
                    }

                    await Task.Delay(5000);
                }
        }
    }
}