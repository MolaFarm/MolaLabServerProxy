using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinFormsComInterop;

namespace ServerProxy;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

internal static class Program
{
    public static Config _config;

    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        try
        {
            ComWrappers.RegisterForMarshalling(WinFormsComWrappers.Instance);
            ApplicationConfiguration.Initialize();

            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                // Read config
                try
                {
                    var rawConf = File.ReadAllText(Application.StartupPath + "/config.json");
                    _config = JsonSerializer.Deserialize(rawConf, SourceGenerationContext.Default.Config);
                }
                catch (Exception ex)
                {
                    _config = new Config
                    {
                        checkUpdate = true,
                        showMessageBoxOnStart = true,
                        serverIP = "114.132.172.176",
                        baseUpdateAddr = "http://IP_ADDRESS_START_HERE.38:31080"
                    };
                }

                Application.Run(new Form1());
            }
            else
            {
                var startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = Application.ExecutablePath;
                startInfo.Verb = "runas";
                try
                {
                    Process.Start(startInfo);
                }
                catch
                {
                    return;
                }

                Application.Exit();
            }
        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }
}