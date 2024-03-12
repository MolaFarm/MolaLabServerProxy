using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CurlThin;
using CurlThin.Enums;
using ServerProxy.ViewModels;

namespace ServerProxy.Tools;

/// <summary>
///     Helper class for managing routes and obtaining available IP addresses.
/// </summary>
public class RouteHelper
{
	/// <summary>
	///     Asynchronously retrieves an available IP address.
	/// </summary>
	/// <returns>A task representing the asynchronous operation. The result is the available IP address.</returns>
	public static async Task<IPAddress> GetAvailableIP()
    {
        var serverIp =
            Dispatcher.UIThread.Invoke(() => (Application.Current.DataContext as AppViewModel).AppConfig.ServerIp);
        var global = CurlNative.Init();
        var easy = CurlNative.Easy.Init();
        string resp;

        try
        {
            CurlNative.Easy.SetOpt(easy, CURLoption.URL, $"https://{serverIp}/dns-query?name=panel.labserver.internal");
            CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
            CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);
            var stream = new MemoryStream();
            CurlNative.Easy.SetOpt(easy, CURLoption.WRITEFUNCTION, (data, size, nmemb, user) =>
            {
                var length = (int)size * (int)nmemb;
                var buffer = new byte[length];
                Marshal.Copy(data, buffer, 0, length);
                stream.Write(buffer, 0, length);
                return (UIntPtr)length;
            });

            var result = CurlNative.Easy.Perform(easy);
            resp = Encoding.UTF8.GetString(stream.ToArray());
        }
        finally
        {
            easy.Dispose();
        }

        using var doc = JsonDocument.Parse(resp);
        IPAddress? internalIp = null;
        foreach (var e in doc.RootElement.GetProperty("Answer").EnumerateArray())
        {
            var ip = e.GetProperty("data").ToString();
            if (ip.StartsWith("IP_ADDRESS_START_HERE"))
            {
                global = CurlNative.Init();
                easy = CurlNative.Easy.Init();

                try
                {
                    CurlNative.Easy.SetOpt(easy, CURLoption.URL, $"https://{ip}/generate_204");
                    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
                    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);
                    var stream = new MemoryStream();
                    CurlNative.Easy.SetOpt(easy, CURLoption.WRITEFUNCTION, (data, size, nmemb, user) =>
                    {
                        var length = (int)size * (int)nmemb;
                        var buffer = new byte[length];
                        Marshal.Copy(data, buffer, 0, length);
                        stream.Write(buffer, 0, length);
                        return (UIntPtr)length;
                    });

                    var result = CurlNative.Easy.Perform(easy);
                    if (result == CURLcode.OK) internalIp = IPAddress.Parse(ip);
                }
                finally
                {
                    easy.Dispose();
                }
            }
        }

        return internalIp ?? IPAddress.Parse(serverIp);
    }
}