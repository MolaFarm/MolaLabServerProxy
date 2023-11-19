using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Ae.Dns.Client;
using Ae.Dns.Protocol;
using Ae.Dns.Protocol.Records;
using Avalonia;
using Avalonia.Threading;
using MsBox.Avalonia.Enums;
using ServerProxy.Proxy;
using ServerProxy.ViewModels;

namespace ServerProxy.Tools;

public class VersionInfo
{
    public string? CommitSha { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string? DownloadAddress { get; set; }
}

public class Updater(string baseAddress)
{
    private const string AccessToken = "GITLAB_ACCESS_TOKEN_HERE";
    private const int ProjectID = 80;
    private readonly Architecture _currentArchitecture = RuntimeInformation.ProcessArchitecture;

    public async Task CheckUpdate()
    {
        try
        {
            await Check();
        }
        catch (Exception ex)
        {
            MessageBox.Show("更新检查失败",
                $"""
                 更新检查失败，请检查配置文件中的 `baseUpdateAddr` 是否正确，并确保没有设置系统代理
                 错误信息: {ex.Message}

                 错误回溯:
                 {ex.StackTrace}
                 """,
                ButtonEnum.Ok, Icon.Warning);
        }
    }

    private async Task Check()
    {
        // Wait for DNS Ready
        while (App.ServiceStatus != Status.Healthy || !App.BroadcastReceiver.IsReceivedOnce)
        {
            if (App.IsOnExit) return;
            await Task.Delay(1000);
        }

        var latestVersion = GetVersionInfo(out var currentVersion);

        if (DateTime.Compare(latestVersion.ReleaseDate, currentVersion.ReleaseDate) <= 0) return;

        var newVerMessage = currentVersion.ReleaseDate != DateTime.MinValue
            ? "检测到新版本，是否要进行更新？"
            : "检测到当前正在使用孤立/开发版本，是否要更新到最新的正式版本？";

        var result = MessageBox.Show("检测到新版本",
            $"""
             {newVerMessage}
             我们建议新版本发布时尽快进行更新，以保证服务器能够正常访问
             更新需要手动进行，当按下“是”时，程序将自动更新

             新版本哈希: {latestVersion.CommitSha}
             发布日期(UTC标准时间): {latestVersion.ReleaseDate.ToString(CultureInfo.InvariantCulture)}
             """, ButtonEnum.YesNo,
            currentVersion.ReleaseDate != DateTime.MinValue ? Icon.Info : Icon.Warning);

        if (result == ButtonResult.Yes)
        {
            LaunchUpdater(latestVersion);
            Dispatcher.UIThread.Invoke(App.OnExit);
        }
    }

    // New method to get version information
    public VersionInfo GetVersionInfo(out VersionInfo currentVersion, string? tagName = null, string? commitSha = null)
    {
        using var client = CreateHttpClient();
        var url = $"api/v4/projects/{ProjectID}/releases";
        var response = Dispatcher.UIThread.Invoke(() => Awaiter.AwaitByPushFrame(client.GetAsync(url)));
        response.EnsureSuccessStatusCode();
        var resp = Dispatcher.UIThread.Invoke(() => Awaiter.AwaitByPushFrame(response.Content.ReadAsStringAsync()));
        using var doc = JsonDocument.Parse(resp);
        var info = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
        VersionInfo? targetVersion = null;
        currentVersion = new VersionInfo
        {
            CommitSha = info.ProductVersion.Split("Sha.")[1]
        };

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var downloadAddr = e.GetProperty("assets").GetProperty("links")
                .EnumerateArray()
                .FirstOrDefault(link =>
                    (_currentArchitecture == Architecture.X64 && link.GetProperty("name").GetString() == "Win64") ||
                    (_currentArchitecture == Architecture.Arm64 && link.GetProperty("name").GetString() == "ARM64"))
                .GetProperty("url").GetString();

            var versionInfo = new VersionInfo
            {
                CommitSha = e.GetProperty("commit").GetProperty("id").GetString(),
                ReleaseDate = e.GetProperty("released_at").GetDateTime(),
                DownloadAddress = downloadAddr
            };

            if ((tagName != null && tagName.Equals(e.GetProperty("tag_name").GetString())) ||
                (commitSha != null && commitSha.Equals(versionInfo.CommitSha)) ||
                (tagName == null && commitSha == null && targetVersion == null))
                targetVersion = versionInfo;

            if (e.GetProperty("commit").GetProperty("id").GetString() == currentVersion.CommitSha)
                currentVersion.ReleaseDate = e.GetProperty("released_at").GetDateTime();

            if (!currentVersion.ReleaseDate.Equals(DateTime.MinValue) && targetVersion != null) break;
        }

        // Return the first element if no specific tag or commitSha is provided
        return targetVersion ?? throw new InvalidOperationException("No matching version found");
    }

    public static void LaunchUpdater(VersionInfo versionInfo, bool force = false)
    {
        DnsMessage? answer;
        try
        {
            using IDnsClient dnsClient = new DnsUdpClient(IPAddress.Loopback);
            answer = Dispatcher.UIThread.Invoke(() => Awaiter.AwaitByPushFrame(
                dnsClient.Query(DnsQueryFactory.CreateQuery("git.labserver.internal"))));
        }
        catch (Exception ex)
        {
            if (!force) throw;
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
            };
            using IDnsClient dnsClient = new CustomDnsHttpClient(new HttpClient(handler)
                { BaseAddress = new Uri((Application.Current.DataContext as AppViewModel).AppConfig.ServerIp) });
            answer = Dispatcher.UIThread.Invoke(() => Awaiter.AwaitByPushFrame(
                dnsClient.Query(DnsQueryFactory.CreateQuery("git.labserver.internal"))));
        }

        if (answer.Answers.Count == 0) throw new Exception("无法找到服务器 IP 地址");
        Process.Start(new ProcessStartInfo
        {
            FileName = $"{Path.GetDirectoryName(Environment.ProcessPath)}\\Updater.exe",
            UseShellExecute = true,
            Arguments =
                $"{AccessToken} {ProjectID} {(answer.Answers[0].Resource as DnsIpAddressResource).IPAddress} {versionInfo.DownloadAddress} {Path.GetDirectoryName(Environment.ProcessPath)}",
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)
        });
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress)
        };

        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", AccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }
}