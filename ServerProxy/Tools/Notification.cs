using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ServerProxy.Tools;

/// <summary>
///     Provides a utility for displaying notifications using a native library.
/// </summary>
internal static class Notification
{
	/// <summary>
	///     External method declaration for showing a notification using a native library.
	/// </summary>
	/// <param name="appname">Byte array representing the application name.</param>
	/// <param name="title">Byte array representing the notification title.</param>
	/// <param name="message">Byte array representing the notification message.</param>
	[DllImport("ToastNotification.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Show(byte[] appname, byte[] title, byte[] message);

	/// <summary>
	///     Displays a notification with the specified title and message.
	/// </summary>
	/// <param name="title">The title of the notification.</param>
	/// <param name="message">The message of the notification.</param>
	public static void Show(string? title, string? message)
    {
        var appnameBytes = Encoding.UTF8.GetBytes("ServerProxy");
        var titleBytes = Encoding.UTF8.GetBytes(title);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        if (OperatingSystem.IsWindows()) Show(appnameBytes, titleBytes, messageBytes);
    }
}