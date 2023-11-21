using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using MsBox.Avalonia.Enums;
using ServerProxy.Broadcast;
using ServerProxy.Tools;

namespace ServerProxy;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BroadCastMessage))]
[JsonSerializable(typeof(Config))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

internal static class Program
{
    public static bool MutexAvailability;
    private static AppBuilder _appBuilder;

    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        const string programUuid = "00079740-26a3-4732-9065-772e81ea93b5";

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        try
        {
            using (new Mutex(true, programUuid, out MutexAvailability))
            {
                _appBuilder = BuildAvaloniaApp().SetupWithLifetime(lifetime);

                if (MutexAvailability)
                {
                    var processes = Process.GetProcessesByName("ServerProxy");
                    foreach (var process in processes)
                        if (process.MainModule?.FileName ==
                            Path.GetDirectoryName(Environment.ProcessPath) + "\\Updater.exe")
                            process.WaitForExit();
                    var newUpdaterExe =
                        new DirectoryInfo(Path.GetDirectoryName(Environment.ProcessPath)).GetFiles("Updater.exe.new");
                    if (newUpdaterExe.Length > 0)
                    {
                        File.Delete(Path.GetDirectoryName(Environment.ProcessPath) + "\\Updater.exe");
                        newUpdaterExe[0].MoveTo(Path.GetDirectoryName(Environment.ProcessPath) + "\\Updater.exe");
                    }

                    lifetime.Start(args);
                }
                else
                {
                    MessageBox.Show("检测到互斥锁", "程序已在运行，不允许同时允许多个程序实例", ButtonEnum.Ok, Icon.Error);
                    Environment.Exit(-1);
                }
            }
        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI()
                .UseWin32()
                .With(new Win32PlatformOptions
                {
                    RenderingMode = new[] { Win32RenderingMode.Software }
                });

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}