using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServerProxy.Proxy;

internal class UdpConnection
{
    private static ILogger _logger;
    private readonly UdpClient _forwardClient;
    private readonly TaskCompletionSource<bool> _forwardConnectionBindCompleted = new();
    private readonly UdpClient _localServer;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly EndPoint? _serverLocalEndpoint;
    private readonly IPEndPoint _sourceEndpoint;
    private EndPoint? _forwardLocalEndpoint;
    private bool _isRunning;
    private long _totalBytesForwarded;
    private long _totalBytesResponded;

    public UdpConnection(UdpClient localServer, IPEndPoint sourceEndpoint, IPEndPoint remoteEndpoint)
    {
        _localServer = localServer;
        _serverLocalEndpoint = _localServer.Client.LocalEndPoint;

        _isRunning = true;
        _remoteEndpoint = remoteEndpoint;
        _sourceEndpoint = sourceEndpoint;

        _logger = App.AppLoggerFactory.CreateLogger<UdpConnection>();

        _forwardClient = new UdpClient(AddressFamily.InterNetworkV6);
        _forwardClient.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
    }

    public long LastActivity { get; private set; } = Environment.TickCount64;

    public async Task SendToServerAsync(byte[] message)
    {
        LastActivity = Environment.TickCount64;

        await _forwardConnectionBindCompleted.Task.ConfigureAwait(false);
        var sent = await _forwardClient.SendAsync(message, message.Length, _remoteEndpoint).ConfigureAwait(false);
        Interlocked.Add(ref _totalBytesForwarded, sent);
    }

    public void Run()
    {
        Task.Run(async () =>
        {
            using (_forwardClient)
            {
                _forwardClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                _forwardLocalEndpoint = _forwardClient.Client.LocalEndPoint;
                _forwardConnectionBindCompleted.SetResult(true);
                _logger.LogInformation(
                    $"Established UDP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}");

                while (_isRunning)
                    try
                    {
                        var result = await _forwardClient.ReceiveAsync().ConfigureAwait(false);
                        LastActivity = Environment.TickCount64;
                        var sent = await _localServer.SendAsync(result.Buffer, result.Buffer.Length, _sourceEndpoint)
                            .ConfigureAwait(false);
                        Interlocked.Add(ref _totalBytesResponded, sent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "An exception occurred on receiving a client datagram");
                    }
            }
        });
    }

    public void Stop()
    {
        try
        {
            _logger.LogInformation(
                $"Closed UDP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}. {_totalBytesForwarded} bytes forwarded, {_totalBytesResponded} bytes responded.");
            _isRunning = false;
            _forwardClient.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An exception occurred on receiving a client datagram");
        }
    }
}