﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using CurlThin;
using CurlThin.Enums;
using CurlThin.SafeHandles;
using Microsoft.Extensions.Logging;
using MsBox.Avalonia.Enums;

namespace ServerProxy.Tools;

public class Updater
{
    private const string AccessToken = "GITLAB_ACCESS_TOKEN_HERE";
    private const int ProjectID = 80;
    private static string _baseAddress;
    private readonly Architecture _currentArchitecture = RuntimeInformation.ProcessArchitecture;

    public Updater(string baseAddress)
    {
        _baseAddress = baseAddress;
    }

    public void CheckUpdate()
    {
        try
        {
            App.IsServiceHealthy.Wait();
            App.BroadcastReceiver.IsReceiveOnce.Wait();
            if (App.UpdaterTokenSource.IsCancellationRequested) return;
            var logger = App.AppLoggerFactory.CreateLogger<Updater>();
            var latestVersion = GetVersionInfo(out var currentVersion);

            if (DateTime.Compare(latestVersion.ReleaseDate, currentVersion.ReleaseDate) <= 0) return;

            logger.LogWarning($"""
                               New version detected:
                               * Current Version:
                                 Commit: {currentVersion.CommitSha}
                                 Release Date: {(currentVersion.ReleaseDate != DateTime.MinValue ? currentVersion.ReleaseDate : "N/A")}
                               * Latest Version:
                                 Commit: {latestVersion.CommitSha}
                                 Release Date: {latestVersion.ReleaseDate}
                               """);

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

    public VersionInfo GetVersionInfo(out VersionInfo currentVersion, string? tagName = null, string? commitSha = null)
    {
        var hostname = new Uri(_baseAddress).Host;
        var serverIp = Awaiter.AwaitByPushFrame(RouteHelper.GetAvailableIP());
        var easy = CurlNative.Easy.Init();

        CurlNative.Easy.SetOpt(easy, CURLoption.URL,
            $"{_baseAddress.Replace(hostname, serverIp.ToString())}/api/v4/projects/{ProjectID}/releases");
        CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
        CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);
        var headers = CurlNative.Slist.Append(SafeSlistHandle.Null, $"Host: {hostname}");
        CurlNative.Slist.Append(headers, $"PRIVATE-TOKEN: {AccessToken}");
        CurlNative.Slist.Append(headers, "Accept: application/json");
        CurlNative.Easy.SetOpt(easy, CURLoption.HTTPHEADER, headers.DangerousGetHandle());

        var stream = new MemoryStream();
        CurlNative.Easy.SetOpt(easy, CURLoption.WRITEFUNCTION, (data, size, nmemb, user) =>
        {
            var length = (int)size * (int)nmemb;
            var buffer = new byte[length];
            Marshal.Copy(data, buffer, 0, length);
            stream.Write(buffer, 0, length);
            return (UIntPtr)length;
        });

        var result = Dispatcher.UIThread.Invoke(() => { return App.HttpHelper.HttpGet(easy); });
        CurlNative.Slist.FreeAll(headers);

        using var doc = JsonDocument.Parse(result.data);
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
        var serverIp = Awaiter.AwaitByPushFrame(RouteHelper.GetAvailableIP());
        Process.Start(new ProcessStartInfo
        {
            FileName = $"{Path.GetDirectoryName(Environment.ProcessPath)}\\Updater.exe",
            UseShellExecute = true,
            Arguments =
                $"{AccessToken} {ProjectID} {serverIp} {versionInfo.DownloadAddress} {Path.GetDirectoryName(Environment.ProcessPath)}",
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)
        });
    }
}