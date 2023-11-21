using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.Caching;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Ae.Dns.Client;
using Ae.Dns.Client.Filters;
using Ae.Dns.Protocol;
using Ae.Dns.Protocol.Enums;
using Ae.Dns.Protocol.Records;
using Ae.Dns.Server;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsBox.Avalonia.Enums;
using ServerProxy.Tools;

namespace ServerProxy.Proxy;

public struct ServiceInfo
{
    public bool IsStarted;
    public bool IsExist;
    public ServiceStartMode StartType;
}

internal class DnsProxy
{
    public static ServiceInfo HnsOriginalStatus = new()
    {
        IsStarted = false,
        IsExist = true,
        StartType = ServiceStartMode.Manual
    };

    public static ServiceInfo SharedAccessOriginalStatus = new()
    {
        IsStarted = false,
        IsExist = true,
        StartType = ServiceStartMode.Manual
    };

    private static ILogger<DnsProxy> _logger;
    private static HttpClient _httpClientInstance;
    private static IDnsFilter _dnsFilter;
    private static IDnsClient _dnsClient;
    private static IDnsClient _cacheClient;
    private static IDnsClient _filterClient;
    private static IOptions<DnsUdpServerOptions> _udpServerOptions;
    private static IOptions<DnsTcpServerOptions> _tcpServerOptions;
    private static IDnsRawClient _rawClient;
    private static IDnsServer _udpDnsServer;
    private static IDnsServer _tcpDnsServer;

    private readonly string _address;
    private readonly Config _config;
    private readonly ServiceController _hnsController;
    private readonly ServiceController _sharedAccessController;
    private readonly ServiceController? _wslServiceController;

    public DnsProxy(string address, Config config)
    {
        _address = address;
        _config = config;
        _logger = App.AppLoggerFactory.CreateLogger<DnsProxy>();
        _sharedAccessController = new ServiceController("SharedAccess");
        _hnsController = new ServiceController("hns");

        try
        {
            var wslServiceController = new ServiceController("wslservice");
            _ = wslServiceController.DisplayName;
            _wslServiceController = wslServiceController;
        }
        catch (Exception)
        {
            // ignored
        }
    }

    public async Task StartAsync()
    {
        TryUpdateServiceStatus(_hnsController, HnsOriginalStatus);

        if (HnsOriginalStatus.IsExist && _hnsController.StartType == ServiceStartMode.Disabled)
        {
            _logger.LogWarning("Possible Dirty Close Detected");
            var dialogResult = MessageBox.Show("注意",
                "检测到\"主机网络服务\"状态为\"禁用\"，这可能是因为上次没有正确关闭本程序引起的，如果属实，请点击\"是\"，程序将在退出时还原设置",
                ButtonEnum.YesNo, Icon.Question);
            if (dialogResult == ButtonResult.Yes)
            {
                HnsOriginalStatus.IsStarted = true;
                HnsOriginalStatus.StartType = ServiceStartMode.Manual;
            }
        }

        if (PortInUse(53))
            try
            {
                if (HnsOriginalStatus.IsExist && _hnsController.Status == ServiceControllerStatus.Running)
                    HandleHnsService();
                else
                    HandleSharedAccessService();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Port 53(TCP/UDP) is occupied");
                SharedAccessOriginalStatus.IsExist = false;
                MessageBox.Show("警告", "检测到 TCP/UDP 53 端口被未知程序占用，代理服务可能无法正常工作", ButtonEnum.Ok, Icon.Warning);
            }

        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        var isInInternalNet = await RouteHelper();
        if (!isInInternalNet)
            Notification.Show("警告", "由于校园网线路不可用，将使用公网线路，连接速度和质量将受到影响");

        _httpClientInstance = new HttpClient(handler) { BaseAddress = new Uri(_address) };
        _dnsFilter = new DnsDelegateFilter(x => true);
        _dnsClient = new CustomDnsHttpClient(_httpClientInstance) { IsInInternalNet = isInInternalNet };
        _cacheClient = new DnsCachingClient(_dnsClient, new MemoryCache("dns"));
        _filterClient = new DnsFilterClient(_dnsFilter, _cacheClient);
        _udpServerOptions =
            Options.Create(new DnsUdpServerOptions { Endpoint = new IPEndPoint(IPAddress.Loopback, 53) });
        _tcpServerOptions =
            Options.Create(new DnsTcpServerOptions { Endpoint = new IPEndPoint(IPAddress.Loopback, 53) });


        _rawClient = new DnsRawClient(_filterClient);

        var udpDnsServerLogger = App.AppLoggerFactory.CreateLogger<DnsUdpServer>();
        var tcpDnsServerLogger = App.AppLoggerFactory.CreateLogger<DnsTcpServer>();
        _udpDnsServer = new DnsUdpServer(udpDnsServerLogger, _rawClient, _udpServerOptions);
        _tcpDnsServer = new DnsTcpServer(tcpDnsServerLogger, _rawClient, _tcpServerOptions);

        var udpServerListener = _udpDnsServer.Listen(App.ProxyTokenSource.Token);
        var tcpServerListener = _tcpDnsServer.Listen(App.ProxyTokenSource.Token);

        var checker = new Thread(HealthChecker);
        checker.Start();

        var v6Proxy = Adapter.ShouldProxyV6();

        if (v6Proxy)
        {
            IPv6Forwarder forwardproxy = new();
            var forwarder = forwardproxy.Start(IPAddress.Loopback, 53, IPAddress.IPv6Loopback, 53);
        }
        else
        {
            Console.WriteLine("Ipv6 disable, DNS proxy will not start");
        }

        Notification.Show("代理服务", "代理服务已启动");

        await udpServerListener;
        await tcpServerListener;
    }

    private static void TryUpdateServiceStatus(ServiceController controller, ServiceInfo serviceInfo)
    {
        try
        {
            _ = controller.DisplayName;
        }
        catch (Exception)
        {
            serviceInfo.IsExist = false;
        }
    }

    private void HandleHnsService()
    {
        if (_config.ShowMessageBoxOnStart)
            MessageBox.Show("警告",
                _wslServiceController != null
                    ? "检测到您的系统中已安装\"适用于 Linux 的 Windows 子系统\"，由于该服务需要需要使用到\"Internet Connection Share\"这一与本程序冲突的功能启动，程序运行过程中 WSL 可能会无法正常启动，如果要用到 WSL，请务必提前启动，本程序会接管\"Internet Connection Share\"的工作，不会影响 WSL 正常工作"
                    : "检测到\"主机网络服务\"正在运行，这会破坏本程序的功能，程序将暂时杀死该服务，由于会影响到 Windows 虚拟化网络服务的工作（包括 Linux 子系统等功能），退出程序时请正常关闭该程序，程序退出时将恢复该服务的状态，在程序的工作期间，所有需要 Windows 提供主机服务的功能将全部不可用。\n\n无法正常工作的功能：移动热点、Hyper-V 网络虚拟化、WSL",
                ButtonEnum.Ok, Icon.Warning);

        HnsOriginalStatus.IsStarted = true;
        ServiceStartModeChanger.Change(_hnsController, ServiceStartMode.Disabled);
        _hnsController.Stop();
        _hnsController.WaitForStatus(ServiceControllerStatus.Stopped);
        _sharedAccessController.Stop();
        _sharedAccessController.WaitForStatus(ServiceControllerStatus.Stopped);
    }

    private void HandleSharedAccessService()
    {
        _ = _sharedAccessController.DisplayName;
        SharedAccessOriginalStatus.IsStarted = true;
        _sharedAccessController.Stop();
        _sharedAccessController.WaitForStatus(ServiceControllerStatus.Stopped);
    }

    private async Task<bool> RouteHelper()
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(_address) };

        using IDnsClient dnsClient = new CustomDnsHttpClient(httpClient) { IsInInternalNet = true };
        var answer = await dnsClient.Query(DnsQueryFactory.CreateQuery("git.labserver.internal"));

        if (answer.Header.AnswerRecordCount > 0 && answer.Answers[0].Type == DnsQueryType.A)
        {
            using var httpGenerate204Client = new HttpClient(handler)
            {
                BaseAddress =
                    new Uri($"https://{((DnsIpAddressResource)answer.Answers[0].Resource).IPAddress}")
            };
            using var response = await httpGenerate204Client.GetAsync("generate_204");
            if (response.StatusCode == HttpStatusCode.NoContent) return true;
        }

        return false;
    }

    public void ServiceRestore()
    {
        if (SharedAccessOriginalStatus is { IsStarted: true, IsExist: true })
        {
            ServiceStartModeChanger.Change(_sharedAccessController, _sharedAccessController.StartType);
            _sharedAccessController.Start();
            _sharedAccessController.WaitForStatus(ServiceControllerStatus.Running);
        }

        if (HnsOriginalStatus is { IsStarted: true, IsExist: true })
        {
            ServiceStartModeChanger.Change(_hnsController, HnsOriginalStatus.StartType);
            _hnsController.Start();
            _hnsController.WaitForStatus(ServiceControllerStatus.Running);
        }

        if (_wslServiceController == null || _wslServiceController.Status == ServiceControllerStatus.Stopped) return;

        var result = MessageBox.Show("提示",
            "在测试中我们发现 WSL 可能会在程序退出后可能无法正常启动，如果你也遇到了这个问题，请点击\"是\"，我们将在退出时进行修复，注意修复时 WSL 会被重启，请确保当前数据已经保存好",
            ButtonEnum.YesNo, Icon.Info);

        if (result == ButtonResult.Yes)
        {
            _wslServiceController.Stop();
            _wslServiceController.WaitForStatus(ServiceControllerStatus.Stopped);
            _wslServiceController.Start();
            _wslServiceController.WaitForStatus(ServiceControllerStatus.Running);
        }
    }

    private static void HealthChecker()
    {
        using IDnsClient dnsClient = new DnsUdpClient(IPAddress.Loopback);
        var lastStatus = Status.Starting;
        var currentStatus = Status.Starting;

        while (true)
        {
            if (App.ProxyTokenSource.IsCancellationRequested) return;
            try
            {
                var answer = Dispatcher.UIThread.Invoke(() => Awaiter.AwaitByPushFrame(
                    dnsClient.Query(DnsQueryFactory.CreateQuery("git.labserver.internal"))));
                currentStatus = Status.Healthy;
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "UnHealthy connection detected!");
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

                Thread.Sleep(5000);
            }
        }
    }

    private static bool PortInUse(int port)
    {
        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
        var ipUdpEndPoints = ipProperties.GetActiveUdpListeners();
        var ipTcpEndPoints = ipProperties.GetActiveTcpListeners();

        return ipUdpEndPoints.Any(endPoint =>
                   endPoint.Port == port &&
                   endPoint.Address.Equals(IPAddress.Loopback)) &&
               ipTcpEndPoints.Any(endPoint =>
                   endPoint.Port == port &&
                   endPoint.Address.Equals(IPAddress.Loopback));
    }
}