using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using ServerProxy.Broadcast;
using ServerProxy.Controls;
using ServerProxy.Proxy;
using ServerProxy.Tools;
using ServerProxy.ViewModels;

namespace ServerProxy;

public class App : Application
{
    private static MixProtocolServer _mixProtocolServer;
    public static cURLHelper HttpHelper;
    public static Receiver BroadcastReceiver;
    public static ILoggerFactory AppLoggerFactory;
    public static CancellationTokenSource ProxyTokenSource = new();
    public static CancellationTokenSource UpdaterTokenSource = new();
    public static Updater UpdaterInstance;
    public static Status ServiceStatus = Status.Starting;
    public static ManualResetEventSlim IsServiceHealthy = new(false);

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

            if ((DataContext as AppViewModel).ShowDebugConsoleOnStart && OperatingSystem.IsWindows()) DebugConsole.AllocConsole();

            if (Program.MutexAvailability)
            {
                var config = (DataContext as AppViewModel).AppConfig;

                // Create Logger Factory
                AppLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

                // Install Certificate
                CertificateUtil.Install();

                // Initialize Http Helper
                HttpHelper = new cURLHelper();

                // Register Broadcast Receiver
                BroadcastReceiver = new Receiver(new Uri($"https://{config.ServerIp}/"));
                _ = Task.Run(BroadcastReceiver.ReceiveBroadcastAsync).ConfigureAwait(false);

                // Start Proxy
                _mixProtocolServer = new MixProtocolServer(config.ServerPort, config.ListeningPort);
                _ = Task.Run(_mixProtocolServer.StartAsync, ProxyTokenSource.Token).ConfigureAwait(false);

                // Check for update
                if (config.CheckUpdate)
                {
                    UpdaterInstance = new Updater(config.BaseUpdateAddr);
                    _ = Task.Run(UpdaterInstance.CheckUpdate, UpdaterTokenSource.Token).ConfigureAwait(false);
                }
            }
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
        if (status == Status.Healthy) IsServiceHealthy.Set();
    }

    public static void OnExit()
    {
        ProxyTokenSource.Cancel();
        UpdaterTokenSource.Cancel();
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

        WriteConfig();
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

    private void ReadBroadCastMessage(object? sender, EventArgs e)
    {
        if (BroadcastReceiver.Message == null)
        {
            MessageBox.Show("广播信息", "没有来自服务器的通知");
            return;
        }

        BroadcastReceiver.Message.Show();
        var currentBroadCastTime = BroadcastReceiver.CurrentBroadCastTime.ToString("O");
        if ((DataContext as AppViewModel).AppConfig.LastReadBroadCast != currentBroadCastTime)
        {
            (DataContext as AppViewModel).AppConfig.LastReadBroadCast = currentBroadCastTime;
            WriteConfig();
        }
    }

    private void WriteConfig()
    {
        var configString = JsonSerializer.Serialize((DataContext as AppViewModel).AppConfig,
            SourceGenerationContext.Default.Config);
        File.WriteAllText(Path.GetDirectoryName(Environment.ProcessPath) + "/config.json", configString);
    }
}