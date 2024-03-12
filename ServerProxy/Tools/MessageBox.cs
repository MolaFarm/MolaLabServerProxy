using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace ServerProxy.Tools;

/// <summary>
///     Provides utility methods for displaying message boxes.
/// </summary>
internal static class MessageBox
{
	/// <summary>
	///     Retrieves system metric information.
	/// </summary>
	/// <param name="nIndex">The system metric index to retrieve.</param>
	/// <returns>The system metric value.</returns>
	[DllImport("user32")]
    private static extern int GetSystemMetrics(int nIndex);

	/// <summary>
	///     Shows a message box with the specified title and message.
	/// </summary>
	/// <param name="title">The title of the message box.</param>
	/// <param name="message">The message to be displayed in the message box.</param>
	/// <param name="button">The button configuration for the message box (default is ButtonEnum.Ok).</param>
	/// <param name="icon">The icon to be displayed in the message box (default is Icon.None).</param>
	/// <returns>The result of the button clicked by the user, or null if no button is clicked.</returns>
	public static ButtonResult? Show(string title, string message, ButtonEnum button = ButtonEnum.Ok,
        Icon icon = Icon.None)
    {
        var screenWidth = 0;
        var screenHeight = 0;

        if (OperatingSystem.IsWindows())
        {
            screenWidth = GetSystemMetrics(0);
            screenHeight = GetSystemMetrics(1);
        }

        if (OperatingSystem.IsLinux())
        {
            // Use xrandr to get size of screen located at offset (0,0).
            var p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "xrandr";
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var match = Regex.Match(output, @"(\d+)x(\d+)\+0\+0");
            var w = match.Groups[1].Value;
            var h = match.Groups[2].Value;
            screenWidth = int.Parse(w);
            screenHeight = int.Parse(h);
        }

        return Dispatcher.UIThread.Invoke(() =>
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ButtonDefinitions = button,
                    CanResize = false,
                    ContentTitle = title,
                    ContentMessage = message,
                    Icon = icon,
                    MaxWidth = screenWidth > screenHeight ? screenWidth / 3 : screenWidth / 2,
                    MaxHeight = screenHeight / 1.5,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Topmost = true
                });

            return Awaiter.AwaitByPushFrame(box.ShowAsync());
        });
    }
}