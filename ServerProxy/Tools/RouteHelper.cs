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
        var easy = CurlNative.Easy.Init();

	    CurlNative.Easy.SetOpt(easy, CURLoption.URL, $"https://{serverIp}/dns-query?name=panel.labserver.internal");
	    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
	    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);
	    var result = Dispatcher.UIThread.Invoke(() =>
	    {
		    return App.HttpHelper.HttpGet(easy);
	    });

	    if (result.response != CURLcode.OK)
	    {
		    throw new Exception($"Failed to get available IP address: {result.response}");
	    }

        using var doc = JsonDocument.Parse(result.data);
        IPAddress? internalIp = null;
        foreach (var e in doc.RootElement.GetProperty("Answer").EnumerateArray())
        {
            var ip = e.GetProperty("data").ToString();
            if (ip.StartsWith("IP_ADDRESS_START_HERE"))
            {
                easy = CurlNative.Easy.Init();

	            CurlNative.Easy.SetOpt(easy, CURLoption.URL, $"https://{ip}/generate_204");
	            CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
	            CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);

	            result = Dispatcher.UIThread.Invoke(() =>
	            {
		            return App.HttpHelper.HttpGet(easy);
	            });

	            if (result.response == CURLcode.OK) internalIp = IPAddress.Parse(ip);
			}
        }

        return internalIp ?? IPAddress.Parse(serverIp);
    }
}