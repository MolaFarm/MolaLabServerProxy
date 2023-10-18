using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
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
    private readonly string _accessToken = "GITLAB_ACCESS_TOKEN_HERE";
    private string _baseAddress;

    public void Check(string baseAddress)
    {
        _baseAddress = baseAddress;
        var gitVersionInformationType =
            Assembly.GetExecutingAssembly().GetType("GitVersionInformation");
        var fields = gitVersionInformationType?.GetFields();
        var currentVersion = new VersionInfo();
        VersionInfo latestVersion = null;
        foreach (var field in fields)
            if (field.Name == "Sha")
                currentVersion.CommitSha = (string?)field.GetValue(null);

        using var client = new HttpClient();
        client.BaseAddress = new Uri(_baseAddress);
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _accessToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        var url = "api/v4/projects/80/releases";
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

        if (DateTime.Compare(latestVersion.ReleaseDate, currentVersion.ReleaseDate) > 0)
        {
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
}