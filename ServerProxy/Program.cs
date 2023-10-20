using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using WinFormsComInterop;

namespace ServerProxy;

internal static class Program
{
    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ComWrappers.RegisterForMarshalling(WinFormsComWrappers.Instance);
        ApplicationConfiguration.Initialize();

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
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
}