using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ServerProxy;

public class VersionInfo
{
    public string? CommitSha;
    public DateTime ReleaseDate;
    public string? ReleasePage;
}

internal class Updater
{
    private const string _accessToken = "GITLAB_ACCESS_TOKEN_HERE";
    private string _baseAddress;

    public void CheckUpdate(string baseAddress)
    {
        try
        {
            Check(baseAddress);
        }
        catch (Exception ex)
        {
            MessageBox.Show("更新检查失败，请检查配置文件中的 `baseUpdateAddr` 是否正确。", "更新检查失败", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void Check(string baseAddress)
    {
        // Wait for DNS Ready
        while (Form1.GetStatus() != Status.Healthy)
        {
            if (Form1.IsOnExit) return;
            Thread.Sleep(1000);
        }

        _baseAddress = baseAddress;
        var info = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);
        VersionInfo latestVersion = null;
        var currentVersion = new VersionInfo
        {
            CommitSha = info.ProductVersion.Split("Sha.")[1]
        };

        var handler = new HttpClientHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback =
            (httpRequestMessage, cert, cetChain, policyErrors) => true;
        using var client = new HttpClient();
        client.BaseAddress = new Uri(_baseAddress);
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        const string url = "api/v4/projects/80/releases";
        var response = client.GetAsync(url).Result;
        response.EnsureSuccessStatusCode();
        var resp = response.Content.ReadAsStringAsync().Result;
        using var doc = JsonDocument.Parse(resp);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            latestVersion ??= new VersionInfo
            {
                CommitSha = e.GetProperty("commit").GetProperty("id").GetString(),
                ReleaseDate = e.GetProperty("released_at").GetDateTime(),
                ReleasePage =
                    $"{_baseAddress}/ShadiaoLeYuan/ServerProxy/-/releases/{e.GetProperty("tag_name").GetString()}"
            };

            if (e.GetProperty("commit").GetProperty("id").GetString() == currentVersion.CommitSha)
                currentVersion.ReleaseDate = e.GetProperty("released_at").GetDateTime();
        }

        if (DateTime.Compare(latestVersion.ReleaseDate, currentVersion.ReleaseDate) <= 0) return;
        var newVerMessage = currentVersion.ReleaseDate != DateTime.MinValue
            ? "检测到新版本，是否要进行更新？"
            : "检测到当前正在使用孤立/开发版本，是否要更新到最新的正式版本？";
        var result = MessageBox.Show($"""
                                      {newVerMessage}
                                      我们建议新版本发布时尽快进行更新，以保证服务器能够正常访问
                                      更新需要手动进行，当按下“是”时，程序将打开新版本下载页面，更新前务必记得退出程序

                                      新版本哈希: {latestVersion.CommitSha}
                                      发布日期(UTC标准时间): {latestVersion.ReleaseDate.ToString(CultureInfo.InvariantCulture)}
                                      """, "检测到新版本", MessageBoxButtons.YesNo,
            currentVersion.ReleaseDate != DateTime.MinValue ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (result == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(latestVersion.ReleasePage) { UseShellExecute = true });
    }
}