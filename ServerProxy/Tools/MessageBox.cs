using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace ServerProxy.Tools;

internal static class MessageBox
{
    [DllImport("user32")]
    private static extern int GetSystemMetrics(int nIndex);

    public static ButtonResult? Show(string title, string message, ButtonEnum button = ButtonEnum.Ok,
        Icon icon = Icon.None)
    {
        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
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