using System.Runtime.InteropServices;
using System.Text;

namespace ServerProxy.Tools;

internal static class Notification
{
    [DllImport("ToastNotification.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Show(byte[] appname, byte[] title, byte[] message);

    public static void Show(string? title, string? message)
    {
        var appnameBytes = Encoding.UTF8.GetBytes("ServerProxy");
        var titleBytes = Encoding.UTF8.GetBytes(title);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        Show(appnameBytes, titleBytes, messageBytes);
    }
}