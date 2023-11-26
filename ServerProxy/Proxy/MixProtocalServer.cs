using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Protocal.Enum.Socks;
using Protocal.Handler.Client;
using Protocal.Request.Socks;
using ServerProxy.Tools;

namespace ServerProxy.Proxy;

internal class MixProtocalServer
{
    private readonly ILogger<MixProtocalServer> _logger;
    private readonly int _serverPort;
    private HttpClientHandler _httpClientHandler;
    private int _listenPort;
    private IPEndPoint _remoteEndpoint;
    private SocksClientHandler _socksClientHandler;

    public MixProtocalServer(int serverPort, int listenPort)
    {
        _serverPort = serverPort;
        _listenPort = listenPort;
        _logger = App.AppLoggerFactory.CreateLogger<MixProtocalServer>();
    }

    public async Task StartAsync()
    {
        var preferIp = await RouteHelper.GetAvailableIP();
        _remoteEndpoint = new IPEndPoint(preferIp, _serverPort);
        _httpClientHandler =
            new HttpClientHandler(_remoteEndpoint, App.AppLoggerFactory.CreateLogger<HttpClientHandler>());
        _socksClientHandler =
            new SocksClientHandler(_remoteEndpoint, App.AppLoggerFactory.CreateLogger<SocksClientHandler>());
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(10);
        _logger.LogInformation($"Server now listening at Loopback:{(listener.LocalEndPoint as IPEndPoint).Port}");

        _ = Task.Run(HealthChecker, App.ProxyTokenSource.Token);

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

    private async Task DetectProtocalAndProxy(Socket client)
    {
        var stream = new NetworkStream(client);
        var buffer = new byte[4096];
        var bytesRead = await stream.ReadAsync(buffer, 0, 4096);
        if (buffer[0] == 0x05 && buffer[1] == (byte)(bytesRead - 2))
            await _socksClientHandler.HandleClientAsync(client, buffer, bytesRead);
        else
            await _httpClientHandler.HandleClientAsync(client, buffer, bytesRead);
    }

    private async Task HealthChecker()
    {
        var lastStatus = Status.Starting;
        var currentStatus = Status.Starting;
        while (!App.ProxyTokenSource.IsCancellationRequested)
            try
            {
                var client = new TcpClient(_remoteEndpoint.Address.ToString(), _remoteEndpoint.Port);
                var stream = client.GetStream();
                var sslStream = new SslStream(stream, false, (sender, certificate, chain, errors) => true);
                await sslStream.AuthenticateAsClientAsync("proxy.labserver.internal");
                var handshakeRequest = new HandShakeRequest
                {
                    Version = 5,
                    AuthMethods = new List<AuthMethod> { AuthMethod.None }
                };
                await sslStream.WriteAsync(handshakeRequest.ToBytes());
                var buffer = new byte[2];
                await sslStream.ReadAsync(buffer);
                if (buffer[0].Equals(5) && buffer[1].Equals(0))
                    currentStatus = Status.Healthy;
                else
                    throw new InvalidDataException();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UnHealthy connection detected!");
                currentStatus = Status.UnHealthy;
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