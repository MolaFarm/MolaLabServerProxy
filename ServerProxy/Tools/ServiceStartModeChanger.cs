using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace ServerProxy.Tools;

internal static class ServiceStartModeChanger
{
    // Change the start mode of a service.
    //
    // Parameters:
    //   service:
    //     The service to be modified.
    //
    //   serviceStartMode:
    //     The new start mode for the service.
    //
    // Remarks:
    //     This function changes the start mode of the specified service by using the
    //     "sc" command. It sets the start mode to either "demand" or the lowercase
    //     string representation of the serviceStartMode parameter. If the output
    //     of the "sc config" command contains the strings "错误" or "ERROR", an exception
    //     with the message "服务启动类型变更失败" is thrown.
    public static void Change(ServiceController service, ServiceStartMode serviceStartMode)
    {
        var startmode = serviceStartMode.Equals(ServiceStartMode.Manual)
            ? "demand"
            : serviceStartMode.ToString().ToLower();
        Run("sc config " + service.ServiceName + " start=" + startmode, out var output, out var error);
        if (output.Contains("错误") || output.Contains("ERROR")) throw new Exception("服务启动类型变更失败");
    }

    // Runs a command in a new process and captures the output and error streams.
    //
    // Parameters:
    //   command: The command to be executed.
    //   output: The captured standard output of the process.
    //   error: The captured standard error of the process.
    //   directory (optional): The working directory of the process. If null, the current
    //   directory is used.
    //
    // Returns: None.
    private static void Run(string command, out string output, out string error, string? directory = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            Arguments = "/c " + command,
            CreateNoWindow = true,
            WorkingDirectory = directory ?? string.Empty
        };
        process.Start();
        process.WaitForExit();
        output = process.StandardOutput.ReadToEnd();
        error = process.StandardError.ReadToEnd();
    }
}