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
        const string programUuid = "00079740-26a3-4732-9065-772e81ea93b5";
        try
        {
            ComWrappers.RegisterForMarshalling(WinFormsComWrappers.Instance);
            ApplicationConfiguration.Initialize();

            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);

            bool createNew;
            using (var mutex = new Mutex(true, programUuid, out createNew))
            {
                if (createNew)
                {
                    // Read config
                    var rawConf = "{}";
                    try
                    {
                        rawConf = File.ReadAllText(Application.StartupPath + "/config.json");
                    }
                    catch (Exception ex)
                    {
                        // ignored
                    }
                    finally
                    {
                        _config = JsonSerializer.Deserialize(rawConf, SourceGenerationContext.Default.Config);
                    }

                    Application.Run(new Form1());
                }
                else
                {
                    MessageBox.Show("程序已在运行，不允许同时允许多个程序实例", "检测到互斥锁", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                }
            }
        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }
}