using Avalonia.Threading;
using Microsoft.Win32;
using MsBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ServerProxy.Tools;

internal class SysProxyHelper
{
    [DllImport("wininet.dll")]
    public static extern bool InternetSetOption
    (IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    public const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    public const int INTERNET_OPTION_REFRESH = 37;

    /// <summary>
    /// TrySetSysProxy is a helper to SetSysProxy, which will check if any application already take control of system proxy
    /// </summary>
    /// <param name="ProxyServer"></param>
    /// <returns></returns>
    public static bool TrySetSysProxy(string ProxyServer)
    {
        int fails = 0;
        var res = GetSysProxy();

        int? enable = res.Item1;
        string? server = res.Item2;
        if (enable.HasValue)
        {
            if (enable == 1)
            {
                if (server != ProxyServer)
                {
                    MessageBoxHelper($"已有软件接管系统代理，代理服务器地址为 {res.Item2} 。请先退出其他软件以避免未知问题发生");
                }
                //Notification.Show("proxyserver", "test!");
                fails++;
            }
        } else{
            fails++;
        }

        if ( fails < 1 )
        {
            SysProxyHelper.SetSysProxy(ProxyServer);
            return true;
        }
        return false;
    }

    /// <summary>
    /// SetSysProxy will change windows' internet proxy setting, enable proxy and set ProxyServer to the
    /// given ProxyServer
    /// </summary>
    /// <param name="ProxyServer">given Server address e.g.127.0.0.1:7890</param>
    public static void SetSysProxy(string ProxyServer)
    {
        RegistryKey registry = Registry.CurrentUser.OpenSubKey
               ("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);

        registry.SetValue("ProxyEnable", 1);
        registry.SetValue("ProxyServer", ProxyServer);
        if ((int)registry.GetValue("ProxyEnable", 0) == 0)
            MessageBoxHelper("Unable to enable the proxy.");
        //else
        //Notification.Show("提示", $"The proxy has been turned on. ProxyServer: {ProxyServer}");
        registry.Close();
        _ = InternetSetOption
        (IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        _ = InternetSetOption
        (IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }

    /// <summary>
    /// GetSysProxy will return nullable values of system proxy settings
    /// </summary>
    /// <returns>(int? ProxyEnabled, string? ProxyServer)</returns>
    public static (int?, string?) GetSysProxy()
    {
        RegistryKey registry = Registry.CurrentUser.OpenSubKey
               ("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
        var en = registry.GetValue("ProxyEnable");
        var server = registry.GetValue("ProxyServer");

        registry.Close();
        _ = InternetSetOption
        (IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        _ = InternetSetOption
        (IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

        string? ProxyServer = server as string;
        int? ProxyEnabled = en as int?;

        return (ProxyEnabled,ProxyServer);
    }

    public static void UnsetSysProxy()
    {
        RegistryKey registry = Registry.CurrentUser.OpenSubKey
               ("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
        registry.SetValue("ProxyEnable", 0);
        registry.SetValue("ProxyServer", 0);
        if ((int)registry.GetValue("ProxyEnable", 1) == 1)
            MessageBoxHelper("Unable to disable the proxy.");
        //else
        //    MessageBoxHelper("success to unset proxy.");
        registry.Close();
        _ = InternetSetOption
        (IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        _ = InternetSetOption
        (IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }

    private static void MessageBoxHelper(string message)
    {
        MessageBox.Show("尝试设置系统代理时", message,ButtonEnum.Ok,Icon.Error);
    }
}
