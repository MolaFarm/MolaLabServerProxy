using System;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CurlThin;
using CurlThin.Enums;
using Microsoft.Extensions.Logging;
using ServerProxy.ViewModels;

namespace ServerProxy.Tools;

/// <summary>
///     Helper class for managing routes and obtaining available IP addresses.
/// </summary>
public class RouteHelper
{
	public static IPAddress? InitializationIpAddress = null;
	public static IPAddress? InternalIpAddress = null;
	public static bool IsUseInternalIpAddress;
	private static ManualResetEventSlim _lockEventSlim = new(false);

	/// <summary>
	///     Asynchronously retrieves an available IP address.
	/// </summary>
	/// <returns>A task representing the asynchronous operation. The result is the available IP address.</returns>
	public static async Task<IPAddress> GetAvailableIP()
    {
	    if (InitializationIpAddress == null)
	    {
		    if (_lockEventSlim.IsSet)
		    {
			    _lockEventSlim.Wait();
		    }
		    else
		    {
				_lockEventSlim.Set();
			    var logger = App.AppLoggerFactory.CreateLogger<RouteHelper>();
			    var serverIp =
				    Dispatcher.UIThread.Invoke(() => (Application.Current.DataContext as AppViewModel).AppConfig.ServerIp);
			    InitializationIpAddress = IPAddress.Parse(serverIp);
			    var easy = CurlNative.Easy.Init();

			    logger.LogInformation($"Initialization Route: {serverIp}");
			    CurlNative.Easy.SetOpt(easy, CURLoption.URL, $"https://{serverIp}/dns-query?name=panel.labserver.internal");
			    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
			    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);
			    var result = Dispatcher.UIThread.Invoke(() => { return App.HttpHelper.HttpGet(easy); });

			    if (result.response != CURLcode.OK)
				    throw new Exception($"Failed to get available IP address: {result.response}");

			    using var doc = JsonDocument.Parse(result.data);
			    foreach (var e in doc.RootElement.GetProperty("Answer").EnumerateArray())
			    {
				    var ip = e.GetProperty("data").ToString();
				    if (ip.StartsWith("IP_ADDRESS_START_HERE"))
				    {
					    logger.LogInformation($"Server returned an available internal IP from the initialization route: {ip}");
					    InternalIpAddress = IPAddress.Parse(ip);
					    easy = CurlNative.Easy.Init();

					    CurlNative.Easy.SetOpt(easy, CURLoption.URL, $"https://{ip}/generate_204");
					    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYPEER, 0);
					    CurlNative.Easy.SetOpt(easy, CURLoption.SSL_VERIFYHOST, 0);

					    result = Dispatcher.UIThread.Invoke(() => { return App.HttpHelper.HttpGet(easy); });

					    if (result.response == CURLcode.OK)
					    {
						    IsUseInternalIpAddress = true;
					    }
					    else
					    {
						    logger.LogWarning($"Cannot access server via {ip}, use initialization route");
					    }
				    }
			    }
			}
		}

	    return IsUseInternalIpAddress ? InternalIpAddress : InitializationIpAddress;
    }
}