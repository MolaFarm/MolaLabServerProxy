﻿using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ServerProxy.Tools;
using ServerProxy.ViewModels;

namespace ServerProxy.Broadcast;

/// <summary>
///     Represents a class for receiving broadcast messages from a server.
/// </summary>
public class Receiver(Uri baseaddr)
{
	/// <summary>
	///     Gets or sets the current broadcast time.
	/// </summary>
	public DateTime CurrentBroadCastTime = DateTime.MinValue;

	/// <summary>
	///     Gets or sets a manual reset event to signal when a broadcast is received.
	/// </summary>
	public ManualResetEventSlim IsReceiveOnce = new(false);

	/// <summary>
	///     Gets or sets the latest broadcast message.
	/// </summary>
	public BroadCastMessage? Message;

	/// <summary>
	///     Creates an HttpClient instance configured with a base address and custom certificate validation.
	/// </summary>
	/// <returns>An HttpClient instance.</returns>
	private HttpClient CreateHttpClient()
	{
		// Configuration for HttpClient with custom certificate validation.
		var handler = new HttpClientHandler
		{
			ClientCertificateOptions = ClientCertificateOption.Manual,
			ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, certChain, policyErrors) => true
		};

		// Create and configure HttpClient.
		var client = new HttpClient(handler)
		{
			BaseAddress = baseaddr
		};

		return client;
	}

	/// <summary>
	///     Asynchronously receives broadcast messages from the server.
	/// </summary>
	/// <returns>A task representing the asynchronous operation.</returns>
	public async Task ReceiveBroadcastAsync()
	{
		// Logger for logging broadcast-related events.
		var logger = App.AppLoggerFactory.CreateLogger<Receiver>();
		using var client = CreateHttpClient();
		const string uri = "broadcast/current";
		var lastReadDate = DateTime.MinValue;

		while (true)
		{
			string responseData;

			try
			{
				// Send a GET request to retrieve the current broadcast time from the server.
				var response = await client.GetAsync(uri);
				response.EnsureSuccessStatusCode();

				// Read the response content as a string.
				responseData = await response.Content.ReadAsStringAsync();
			}
			catch (Exception)
			{
				// If an exception occurs, wait for 10 seconds and then continue the loop.
				await Task.Delay(10000);
				continue;
			}

			// Attempt to parse the response string into a DateTime.
			DateTime.TryParse(responseData, out var newCurrentBroadCastTime);

			// Check if the new broadcast time is later than the current broadcast time.
			if (DateTime.Compare(newCurrentBroadCastTime, CurrentBroadCastTime) > 0)
			{
				// Invoke UI thread to access UI-related operations.
				if (!Dispatcher.UIThread.Invoke(() =>
					    DateTime.TryParse(
						    (Application.Current.DataContext as AppViewModel).AppConfig.LastReadBroadCast,
						    null, DateTimeStyles.AssumeUniversal, out lastReadDate)))
					lastReadDate = DateTime.MinValue;

				// Check if the new broadcast time is later than the last read date.
				if (DateTime.Compare(newCurrentBroadCastTime, lastReadDate) > 0)
				{
					// Retrieve the broadcast message.
					Message = await GetBroadCastMessage();
					Message.Datetime = responseData.TrimEnd();

					// Log and show a notification for the received broadcast.
					logger.LogInformation($"Received Broadcast: {Message.Title}");
					Notification.Show($"收到服务器广播：{Message.Title}", "你可以通过托盘菜单查看完整消息");

					// Check for a force update in the broadcast message.
					if (Message.ForceUpdateTagName != null)
					{
						// Get version information for the force update.
						var targetVersion = App.UpdaterInstance.GetVersionInfo(out var currentVersion,
							Message.ForceUpdateTagName);

						// If the target version is different, initiate the update process.
						if (!targetVersion.CommitSha.Equals(currentVersion.CommitSha))
						{
							await App.UpdaterTokenSource.CancelAsync();
							try
							{
								Updater.LaunchUpdater(targetVersion, true);
							}
							catch (Exception ex)
							{
								await App.ProxyTokenSource.CancelAsync();
								ExceptionHandler.Handle(ex);
							}

							// Cancel the proxy token source.
							if (!App.ProxyTokenSource.IsCancellationRequested)
								await App.ProxyTokenSource.CancelAsync();

							// Log and show a message indicating a force update.
							logger.LogWarning($"Force Update Required! Target Version: {targetVersion.CommitSha}");
							MessageBox.Show("服务器云控",
								$"收到服务器强制更新要求，程序将自动更新！\n\n目标版本哈希：{targetVersion.CommitSha}\n释出日期：{targetVersion.ReleaseDate}");

							// Invoke the UI thread to perform application exit.
							Dispatcher.UIThread.Invoke(App.OnExit);
						}
					}

					// Update the current broadcast time.
					CurrentBroadCastTime = newCurrentBroadCastTime;
				}
				else
				{
					// Update the current broadcast time.
					CurrentBroadCastTime = newCurrentBroadCastTime;
				}
			}

			// If the broadcast message is null, retrieve it and update the datetime.
			if (Message == null)
			{
				Message = await GetBroadCastMessage();
				Message.Datetime = responseData.TrimEnd();
			}

			// Set the manual reset event to signal that a broadcast has been received.
			IsReceiveOnce.Set();

			// Wait for 10 seconds before checking for the next broadcast.
			await Task.Delay(10000);
		}
	}

	/// <summary>
	///     Asynchronously retrieves the broadcast message from the server.
	/// </summary>
	/// <returns>A task representing the asynchronous operation.</returns>
	private async Task<BroadCastMessage?> GetBroadCastMessage()
	{
		using var client = CreateHttpClient();
		const string uri = "broadcast/message";

		// Send a GET request to retrieve the broadcast message.
		var response = await client.GetAsync(uri);
		response.EnsureSuccessStatusCode();

		// Read the response content as a string.
		var resp = await response.Content.ReadAsStringAsync();

		// Parse the JSON string into a BroadCastMessage object.
		using var doc = JsonDocument.Parse(resp);
		return JsonSerializer.Deserialize(resp, SourceGenerationContext.Default.BroadCastMessage);
	}
}