using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace ServerProxy.Tools;

internal static class MessageBox
{
    public static ButtonResult? Show(string title, string message, ButtonEnum button = ButtonEnum.Ok,
        Icon icon = Icon.None)
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard(title, message, button, icon);

            return Awaiter.AwaitByPushFrame(box.ShowAsync());
        });
    }
}