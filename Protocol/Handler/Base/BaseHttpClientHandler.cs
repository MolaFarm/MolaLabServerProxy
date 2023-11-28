using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Protocol.Request.Http;

namespace Protocol.Handler.Base;

/// <summary>
///     Base class for handling HTTP requests from clients.
/// </summary>
public class BaseHttpClientHandler : BaseClientHandler
{
	protected ILogger<BaseHttpClientHandler> Logger;

	/// <summary>
	///     Initializes a new instance of the <see cref="BaseHttpClientHandler" /> class.
	/// </summary>
	/// <param name="logger">The logger for logging HTTP handler events.</param>
	public BaseHttpClientHandler(ILogger<BaseHttpClientHandler> logger)
	{
		Logger = logger;
	}

	/// <summary>
	///     Pipes data between the client and the remote server.
	/// </summary>
	/// <param name="httpRequest">The HTTP request received from the client.</param>
	/// <param name="clientStream">The stream connected to the client.</param>
	/// <param name="remoteStream">The stream connected to the remote server.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	protected static async Task PipeStream(HttpRequest httpRequest, Stream clientStream, Stream remoteStream)
	{
		// Forward data between client and remote server
		if (!httpRequest.RequestType.Equals("CONNECT"))
			await remoteStream.WriteAsync(Encoding.UTF8.GetBytes(httpRequest.RequestData));

		// Forward data between client and remote server
		var pipeTask = PipeStream(clientStream, remoteStream);

		if (httpRequest.RequestType.Equals("CONNECT"))
		{
			// Send a success response to the client
			var successResponse = "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray();
			await clientStream.WriteAsync(successResponse, 0, successResponse.Length);
		}

		await pipeTask;
	}

	/// <summary>
	///     Handles the incoming client connection asynchronously.
	/// </summary>
	/// <param name="client">The client socket.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public override async Task HandleClientAsync(Socket client)
	{
		await using var networkStream = new NetworkStream(client);
		var buffer = new byte[4096];
		var bytesRead = await networkStream.ReadAsync(buffer);

		// Read the HTTP request from the client
		try
		{
			var httpRequest = HttpRequest.FromBytes(buffer, bytesRead);
			if (httpRequest == null)
			{
				Logger.LogWarning("Null request received! Request will be ignore.");
				return;
			}

			await HandleHttpRequest(httpRequest, client);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, "Failed to handle client");
		}
		finally
		{
			// Close the client connection
			client.Close();
		}
	}

	/// <summary>
	///     Handles the HTTP request from the client by establishing a connection to the remote server.
	/// </summary>
	/// <param name="httpRequest">The HTTP request received from the client.</param>
	/// <param name="client">The client socket.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	protected static async Task HandleHttpRequest(HttpRequest httpRequest, Socket client)
	{
		using (var remoteClient = new TcpClient(httpRequest.Host, httpRequest.Port))
		await using (var remoteStream = remoteClient.GetStream())
		await using (var clientStream = new NetworkStream(client))
		{
			await PipeStream(httpRequest, clientStream, remoteStream);
		}
	}
}