using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
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
        var client = CreateHttpClient($"https://{serverIp}/");
        var url = "dns-query?name=panel.labserver.internal";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var resp = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(resp);
        IPAddress? internalIp = null;
        foreach (var e in doc.RootElement.GetProperty("Answer").EnumerateArray())
        {
            var ip = e.GetProperty("data").ToString();
            if (ip.StartsWith("IP_ADDRESS_START_HERE"))
            {
                using var httpGenerate204Client = CreateHttpClient($"https://{ip}/");
                using var httpGenerate204Response = await httpGenerate204Client.GetAsync("generate_204");
                if (httpGenerate204Response.StatusCode == HttpStatusCode.NoContent) internalIp = IPAddress.Parse(ip);
            }
        }

        return internalIp ?? IPAddress.Parse(serverIp);
    }

	/// <summary>
	///     Creates an HttpClient instance with custom certificate validation.
	/// </summary>
	/// <param name="baseAddress">The base address for the HttpClient.</param>
	/// <returns>An HttpClient instance.</returns>
	private static HttpClient CreateHttpClient(string baseAddress)
    {
        var handler = new HttpClientHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseAddress)
        };

        return client;
    }
}