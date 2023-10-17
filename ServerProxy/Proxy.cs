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
    public ServiceStartMode StartType;
}

internal class Proxy
{
    public static ServiceInfo HnsOriginalStatus = new() { IsStarted = false, StartType = ServiceStartMode.Manual };
    private readonly string _address;
    private readonly ServiceController _hnsController;
    private readonly ServiceController _sharedAccessController;

    public Proxy(string address)
    {
        this._address = address;
        _sharedAccessController = new ServiceController("SharedAccess");
        _hnsController = new ServiceController("hns");
    }

    // Starts the proxy service.
    public async void StartProxy()
    {
        // Terminate ICS Service
        if (_hnsController.Status == ServiceControllerStatus.Running)
        {
            MessageBox.Show(
                "检测到\"主机网络服务\"正在运行，这会破坏本程序的功能，程序将暂时杀死该服务，由于会影响到 Windows 虚拟化网络服务的工作（包括 Linux 子系统等功能），退出程序时请正常关闭该程序，程序退出时将恢复该服务的状态，在程序的工作期间，程序将接管\"主机网络服务\"的工作，不会影响绝大多数情况下的正常使用\n\n本程序运行时可能无法正常工作的服务：移动热点",
                "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try
            {
                HnsOriginalStatus.IsStarted = true;
                // Disable HNS Service
                ServiceStartModeChanger.Change(_hnsController, ServiceStartMode.Disabled);
                _hnsController.Stop();
                _hnsController.WaitForStatus(ServiceControllerStatus.Stopped);
                _sharedAccessController.Stop();
                _sharedAccessController.WaitForStatus(ServiceControllerStatus.Stopped);
                for (var i = 0; i < 3; i++)
                    if (PortInUse(53))
                    {
                        if (i == 2)
                            throw new Exception("服务未能按预期停止");
                        Thread.Sleep(1000);
                    }
            }
            catch (Exception ex)
            {
                ExceptionHandler.Handle(ex);
                Environment.Exit(-1);
            }
        }
        else
        {
            var dialogResult = MessageBox.Show("检测到\"主机网络服务\"状态为\"禁用\"，这可能是因为上次没有正确关闭本程序引起的，如果属实，请点击\"是\"，程序将在退出时还原设置",
                "注意", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dialogResult == DialogResult.Yes)
            {
                HnsOriginalStatus.IsStarted = true;
                HnsOriginalStatus.StartType = ServiceStartMode.Manual;
            }
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
            Endpoint = new IPEndPoint(IPAddress.Any, 53)
        };

        // Create a "raw" client (efficiently deals with network buffers)
        using IDnsRawClient rawClient = new DnsRawClient(filterClient);

        // Create the server
        using IDnsServer server = new DnsUdpServer(rawClient, serverOptions);

        var checker = HealthChecker();
        Form1.ShowNotification("代理服务", "代理服务已启动", ToolTipIcon.Info);
        await server.Listen();
        await checker;
    }

    // ServiceRestore is a public void method that restores the service to its original status.
    public void ServiceRestore()
    {
        if (!HnsOriginalStatus.IsStarted) return;
        ServiceStartModeChanger.Change(_hnsController, HnsOriginalStatus.StartType);
        _sharedAccessController.Start();
        _hnsController.Start();
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
                currentStatus = Status.Unhealty;
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
                        case Status.Unhealty:
                            Form1.ShowNotification("警告", "代理服务状态: 连接阻塞", ToolTipIcon.Warning);
                            break;
                    }
                }

                Thread.Sleep(60000);
            }
    }

    // Checks if a given port is already in use.
    private static bool PortInUse(int port)
    {
        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
        var ipEndPoints = ipProperties.GetActiveTcpListeners();

        return ipEndPoints.Any(endPoint => endPoint.Port == port);
    }
}