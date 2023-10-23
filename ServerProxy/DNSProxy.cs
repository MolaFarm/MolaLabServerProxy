using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace ServerProxy;

internal class DNSProxy
{
    /// <summary>
    ///     Milliseconds
    /// </summary>
    public int ConnectionTimeout { get; set; } = 4 * 60 * 1000;

    public async Task Start(IPAddress remoteIp, ushort remotePort, IPAddress localIp, ushort localPort)
    {
        var connections = new ConcurrentDictionary<IPEndPoint, UdpConnection>();

        var remoteServerEndPoint = new IPEndPoint(remoteIp, remotePort);

        var localServer = new UdpClient(AddressFamily.InterNetworkV6);
        localServer.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        localServer.Client.Bind(new IPEndPoint(localIp, localPort));

        Console.WriteLine($"UDP proxy started [{localIp}]:{localPort} -> [{remoteIp}]:{remotePort}");

        var _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                foreach (var connection in connections.ToArray())
                    if (connection.Value.LastActivity + ConnectionTimeout < Environment.TickCount64)
                    {
                        connections.TryRemove(connection.Key, out var c);
                        connection.Value.Stop();
                    }
            }
        });

        while (true)
            try
            {
                var message = await localServer.ReceiveAsync().ConfigureAwait(false);
                var sourceEndPoint = message.RemoteEndPoint;
                var client = connections.GetOrAdd(sourceEndPoint,
                    ep =>
                    {
                        var udpConnection = new UdpConnection(localServer, sourceEndPoint, remoteServerEndPoint);
                        udpConnection.Run();
                        return udpConnection;
                    });
                await client.SendToServerAsync(message.Buffer).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                //ExceptionHandler.Handle(ex);
                Console.WriteLine($"an exception occurred on receiving a client datagram: {ex}");
            }
    }
}

internal class UdpConnection
{
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
                Console.WriteLine(
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
                        //if (_isRunning) ExceptionHandler.Handle(ex);
                        Console.WriteLine($"an exception occurred on receiving a client datagram: {ex}");
                    }
            }
        });
    }

    public void Stop()
    {
        try
        {
            Console.WriteLine(
                $"Closed UDP {_sourceEndpoint} => {_serverLocalEndpoint} => {_forwardLocalEndpoint} => {_remoteEndpoint}. {_totalBytesForwarded} bytes forwarded, {_totalBytesResponded} bytes responded.");
            _isRunning = false;
            _forwardClient.Close();
        }
        catch (Exception ex)
        {
            //ExceptionHandler.Handle(ex);
            Console.WriteLine($"an exception occurred on receiving a client datagram: {ex}");
        }
    }
}