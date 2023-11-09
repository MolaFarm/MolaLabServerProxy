using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using Ae.Dns.Client;
using Ae.Dns.Client.Filters;
using Ae.Dns.Protocol;
using Ae.Dns.Server;

namespace ServerProxy;

public struct ServiceInfo
{
    public bool IsStarted;
    public bool IsExist;
    public ServiceStartMode StartType;
}

internal class Proxy
{
    public static ServiceInfo HnsOriginalStatus = new() { IsStarted = false, IsExist = true, StartType = ServiceStartMode.Manual };
    public static ServiceInfo sharedAccessOriginalStatus = new() { IsStarted = false, IsExist = true, StartType = ServiceStartMode.Manual };
    private readonly string _address;
    private readonly ServiceController _hnsController;
    private readonly ServiceController _sharedAccessController;
    private readonly ServiceController? _wslServiceController;

    public Proxy(string address)
    {
        _address = address;
        _sharedAccessController = new ServiceController("SharedAccess");
        _hnsController = new ServiceController("hns");
        try
        {
            var wslServiceController = new ServiceController("wslservice");
            _ = wslServiceController.DisplayName;
            _wslServiceController = wslServiceController;
        }
        catch (Exception ex)
        {
            // ignored
        }
    }

    // Starts the proxy service.
    public async Task StartProxy()
    {
        // Check if the HNS service is disabled. 
        // It is possible that the HNS service 
        // does not resume working when the program 
        // exits abnormally.
        try
        {
            _ = _hnsController.DisplayName;
        }
        catch (Exception ex)
        {
            HnsOriginalStatus.IsExist = false;
        }

        if (HnsOriginalStatus.IsExist && _hnsController.StartType == ServiceStartMode.Disabled)
        {
            var dialogResult = MessageBox.Show(
                "检测到\"主机网络服务\"状态为\"禁用\"，这可能是因为上次没有正确关闭本程序引起的，如果属实，请点击\"是\"，程序将在退出时还原设置",
                "注意", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Yes)
            {
                HnsOriginalStatus.IsStarted = true;
                HnsOriginalStatus.StartType = ServiceStartMode.Manual;
            }
        }

        if (PortInUse(53))
            try
            {
                // Disable HNS when port is not available
                if (HnsOriginalStatus.IsExist && _hnsController.Status == ServiceControllerStatus.Running)
                {
                    if (Program._config.showMessageBoxOnStart)
                        MessageBox.Show(
                            _wslServiceController != null
                                ? "检测到您的系统中已安装\"适用于 Linux 的 Windows 子系统\"，由于该服务需要需要使用到\"Internet Connection Share\"这一与本程序冲突的功能启动，程序运行过程中 WSL 可能会无法正常启动，如果要用到 WSL，请务必提前启动，本程序会接管\"Internet Connection Share\"的工作，不会影响 WSL 正常工作"
                                : "检测到\"主机网络服务\"正在运行，这会破坏本程序的功能，程序将暂时杀死该服务，由于会影响到 Windows 虚拟化网络服务的工作（包括 Linux 子系统等功能），退出程序时请正常关闭该程序，程序退出时将恢复该服务的状态，在程序的工作期间，所有需要 Windows 提供主机服务的功能将全部不可用。\n\n无法正常工作的功能：移动热点、Hyper-V 网络虚拟化、WSL",
                            "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    HnsOriginalStatus.IsStarted = true;
                    // Disable HNS Service
                    ServiceStartModeChanger.Change(_hnsController, ServiceStartMode.Disabled);
                    _hnsController.Stop();
                    _hnsController.WaitForStatus(ServiceControllerStatus.Stopped);
                    _sharedAccessController.Stop();
                    _sharedAccessController.WaitForStatus(ServiceControllerStatus.Stopped);
                    await Task.Delay(1000);
                }
                else
                {
                    try
                    {
                        _ = _sharedAccessController.DisplayName;
                        sharedAccessOriginalStatus.IsStarted = true;
                        _sharedAccessController.Stop();
                        _sharedAccessController.WaitForStatus(ServiceControllerStatus.Stopped);
                    }
                    catch (Exception ex)
                    {
                        sharedAccessOriginalStatus.IsExist = false;
                        MessageBox.Show("检测到 UDP 53 端口被未知程序占用，代理服务可能无法正常工作", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionHandler.Handle(ex);
            }

        var handler = new HttpClientHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback =
            (httpRequestMessage, cert, cetChain, policyErrors) => true;

        using var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri(_address);

        IDnsFilter dnsFilter = new DnsDelegateFilter(x => true);
        using IDnsClient dnsClient = new DnsHttpClient(httpClient);
        using IDnsClient filterClient = new DnsFilterClient(dnsFilter, dnsClient);

        var serverOptions = new DnsUdpServerOptions
        {
            Endpoint = new IPEndPoint(IPAddress.Loopback, 53)
        };

        // Create a "raw" client (efficiently deals with network buffers)
        using IDnsRawClient rawClient = new DnsRawClient(filterClient);

        // Create the server
        using IDnsServer server = new DnsUdpServer(rawClient, serverOptions);

        var serverListener = server.Listen();
        var checker = HealthChecker();

        var v6proxy = Adapter.ShouldProxyV6();

        if (v6proxy)
        {
            DNSProxy forwardproxy = new();
            var forwarder = forwardproxy.Start(IPAddress.Loopback, 53, IPAddress.IPv6Loopback, 53);
        }
        else
        {
            Console.WriteLine("Ipv6 disable, DNS proxy will not start");
        }

        Form1.ShowNotification("代理服务", "代理服务已启动", ToolTipIcon.Info);

        await serverListener;
        await checker;
    }

    // ServiceRestore is a public void method that restores the service to its original status.
    public void ServiceRestore()
    {
        if (!sharedAccessOriginalStatus.IsStarted || !sharedAccessOriginalStatus.IsExist) return;
        ServiceStartModeChanger.Change(_sharedAccessController, _sharedAccessController.StartType);
        _sharedAccessController.Start();
        _sharedAccessController.WaitForStatus(ServiceControllerStatus.Running);

        if (!HnsOriginalStatus.IsStarted || !HnsOriginalStatus.IsExist) return;
        ServiceStartModeChanger.Change(_hnsController, HnsOriginalStatus.StartType);
        _hnsController.Start();
        _hnsController.WaitForStatus(ServiceControllerStatus.Running);

        if (_wslServiceController == null || _wslServiceController.Status == ServiceControllerStatus.Stopped) return;
        var result = MessageBox.Show(
            "在测试中我们发现 WSL 可能会在程序退出后可能无法正常启动，如果你也遇到了这个问题，请点击\"是\"，我们将在退出时进行修复，注意修复时 WSL 会被重启，请确保当前数据已经保存好",
            "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (result == DialogResult.Yes)
        {
            _wslServiceController.Stop();
            _wslServiceController.WaitForStatus(ServiceControllerStatus.Stopped);
            _wslServiceController.Start();
            _wslServiceController.WaitForStatus(ServiceControllerStatus.Running);
        }
    }

    // Asynchronous task that checks the health of the application.
    private static async Task HealthChecker()
    {
        using IDnsClient dnsClient = new DnsUdpClient(IPAddress.Loopback);
        var lastStatus = Status.Starting;
        var currentStatus = Status.Starting;
        while (true)
            try
            {
                var answer = await dnsClient.Query(DnsQueryFactory.CreateQuery("git.labserver.internal"));
                currentStatus = Status.Healthy;
            }
            catch (Exception ex)
            {
                currentStatus = Status.UnHealthy;
            }
            finally
            {
                if (lastStatus != currentStatus)
                {
                    lastStatus = currentStatus;
                    Form1.SetStatus(currentStatus);
                    switch (currentStatus)
                    {
                        case Status.Healthy:
                            Form1.ShowNotification("提示", "代理服务状态: 健康", ToolTipIcon.Info);
                            break;
                        case Status.UnHealthy:
                            Form1.ShowNotification("警告", "代理服务状态: 连接阻塞", ToolTipIcon.Warning);
                            break;
                    }
                }

                await Task.Delay(60000);
            }
    }

    // Checks if a given port is already in use.
    private static bool PortInUse(int port)
    {
        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
        var ipEndPoints = ipProperties.GetActiveUdpListeners();

        return ipEndPoints.Any(endPoint =>
            endPoint.Port == port &&
            (endPoint.Address.Equals(IPAddress.Loopback)));
    }
}