using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServerProxy.Controls;
using ServerProxy.Proxy;
using ServerProxy.Tools;
using ServerProxy.ViewModels;

namespace ServerProxy;

public class App : Application
{
    private static DnsProxy _proxy;
    private static List<NetworkInterface> _adapters;
    public static Status ServiceStatus = Status.Starting;
    public static bool IsOnExit;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DataContext = new AppViewModel(ActualThemeVariant);
            ActualThemeVariantChanged += (s, e) => (DataContext as AppViewModel).RenderIcon(ActualThemeVariant);

            // Start services
            if ((DataContext as AppViewModel).ShowDebugConsoleOnStart) DebugConsole.AllocConsole();

            // Install Certificate
            CertificateUtil.Install();

            // Create Proxy
            var config = (DataContext as AppViewModel).GetConfig;
            SetStatus(Status.Starting);
            _proxy = new DnsProxy($"https://{config.ServerIp}/", config);
            _ = Task.Run(_proxy.StartAsync);

            // Set DNS
            _adapters = Adapter.ListAllInterface();
            foreach (var adapter in _adapters) Adapter.CSetDns(adapter, "127.0.0.1", "::1");

            // Check for update
            if (!config.CheckUpdate) return;
            var updater = new Updater();
            var updaterThread = new Thread(() => updater.CheckUpdate(config.BaseUpdateAddr));
            updaterThread.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void SetStatus(Status status)
    {
        (DataContext as AppViewModel).StatusMessage = status switch
        {
            Status.Healthy => "健康",
            Status.Starting => "启动中",
            Status.UnHealthy => "连接阻塞",
            _ => (DataContext as AppViewModel).StatusMessage
        };

        ServiceStatus = status;
    }

    private static void OnExit()
    {
        IsOnExit = true;
        foreach (var adapter in _adapters) Adapter.CUnsetDns(adapter);
        if (DnsProxy.HnsOriginalStatus.IsStarted) _proxy.ServiceRestore();
        Environment.Exit(0);
    }

    private void NativeMenuItem_OnClick(object? sender, EventArgs e)
    {
        var item = (NativeMenuItem)sender;
        if (item != null)
            switch (item.Header)
            {
                case "退出":
                    OnExit();
                    break;
            }
    }

    private void NativeMenuItem_OnChecked(object? sender, EventArgs e)
    {
        var item = (CustomMenuItem)sender;

        switch (item.Name)
        {
            case "CheckUpdate":
                (DataContext as AppViewModel).CheckUpdate = !(DataContext as AppViewModel).CheckUpdate;
                break;
            case "ShowMessageBoxOnStart":
                (DataContext as AppViewModel).ShowMessageBoxOnStart =
                    !(DataContext as AppViewModel).ShowMessageBoxOnStart;
                break;
            case "ShowDebugConsoleOnStart":
                (DataContext as AppViewModel).ShowDebugConsoleOnStart =
                    !(DataContext as AppViewModel).ShowDebugConsoleOnStart;
                break;
        }

        var configString = JsonSerializer.Serialize((DataContext as AppViewModel).GetConfig,
            SourceGenerationContext.Default.Config);
        File.WriteAllText(Path.GetDirectoryName(Environment.ProcessPath) + "/config.json", configString);
    }

    private void FastAccess_OnClicked(object? sender, EventArgs e)
    {
        var item = (NativeMenuItem)sender;

        var addr = item.Header switch
        {
            "GitLab" => "https://git.labserver.internal",
            "Coder" => "https://coder.labserver.internal",
            "NextCloud" => "https://cloud.labserver.internal",
            _ => null
        };

        Process.Start(new ProcessStartInfo(addr) { UseShellExecute = true });
    }
}