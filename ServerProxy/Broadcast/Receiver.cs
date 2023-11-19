﻿using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using ServerProxy.Tools;
using ServerProxy.ViewModels;

namespace ServerProxy.Broadcast;

internal class Receiver(Uri baseaddr)
{
    public DateTime CurrentBroadCastTime = DateTime.MinValue;
    public BroadCastMessage? Message;

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = baseaddr
        };

        return client;
    }


    public async Task ReceiveBroadcastAsync()
    {
        using var client = CreateHttpClient();
        const string uri = "broadcast/current";
        var lastReadDate = DateTime.MinValue;
        while (true)
        {
            string responseData;
            try
            {
                var response = await client.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                responseData = await response.Content.ReadAsStringAsync();
            }
            catch (Exception)
            {
                await Task.Delay(10000);
                continue;
            }

            DateTime.TryParse(responseData, out var newCurrentBroadCastTime);
            if (DateTime.Compare(newCurrentBroadCastTime, CurrentBroadCastTime) > 0)
            {
                if (!Dispatcher.UIThread.Invoke(() =>
                        DateTime.TryParse(
                            (Application.Current.DataContext as AppViewModel).AppConfig.LastReadBroadCast,
                            null, DateTimeStyles.AssumeUniversal, out lastReadDate)))
                    lastReadDate = DateTime.MinValue;

                if (DateTime.Compare(newCurrentBroadCastTime, lastReadDate) > 0)
                {
                    Message = await GetBroadCastMessage();
                    Message.Datetime = responseData.TrimEnd();
                    Notification.Show($"收到服务器广播：{Message.Title}", "你可以通过托盘菜单查看完整消息");
                    if (Message.ForceUpdateTagName != null)
                    {
                        var targetVersion = App.UpdaterInstance.GetVersionInfo(out var currentVersion,
                            Message.ForceUpdateTagName);
                        if (!targetVersion.CommitSha.Equals(currentVersion.CommitSha))
                        {
                            App.UpdaterInstance.LaunchUpdater(targetVersion);
                            MessageBox.Show("服务器云控",
                                $"收到服务器强制更新要求，程序将自动更新！\n\n目标版本哈希：{targetVersion.CommitSha}\n释出日期：{targetVersion.ReleaseDate}");
                        }
                    }

                    CurrentBroadCastTime = newCurrentBroadCastTime;
                }
                else
                {
                    CurrentBroadCastTime = newCurrentBroadCastTime;
                }
            }

            if (Message == null)
            {
                Message = await GetBroadCastMessage();
                Message.Datetime = responseData.TrimEnd();
            }

            await Task.Delay(10000);
        }
    }

    private async Task<BroadCastMessage?> GetBroadCastMessage()
    {
        using var client = CreateHttpClient();
        const string uri = "broadcast/message";
        var response = await client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resp);
        return JsonSerializer.Deserialize(resp, SourceGenerationContext.Default.BroadCastMessage);
    }
}