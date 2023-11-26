using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Protocal.Request.Http;

namespace Protocal.Handler.Base;

public class BaseHttpClientHandler : BaseClientHandler
{
    protected ILogger<BaseHttpClientHandler> _logger;

    public BaseHttpClientHandler(ILogger<BaseHttpClientHandler> logger)
    {
        _logger = logger;
    }

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

    public override async Task HandleClientAsync(Socket client)
    {
        using var networkStream = new NetworkStream(client);
        var buffer = new byte[4096];
        var bytesRead = await networkStream.ReadAsync(buffer);

        // Read the HTTP request from the client
        try
        {
            var httpRequest = HttpRequest.FromBytes(buffer, bytesRead);
            if (httpRequest == null)
            {
                _logger.LogWarning("Null request received! Request will be ignore.");
                return;
            }

            await HandleHttpRequest(httpRequest, client);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle client");
        }
        finally
        {
            // Close the client connection
            client.Close();
        }
    }

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