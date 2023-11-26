using System.Net.Sockets;

namespace Protocal.Handler.Base;

public abstract class BaseClientHandler
{
    public abstract Task HandleClientAsync(Socket client);

    protected static async Task PipeStream(Stream clientStream, Stream remoteStream)
    {
        // Forward data between client and remote server
        var clientToRemote = clientStream.CopyToAsync(remoteStream);
        var remoteToClient = remoteStream.CopyToAsync(clientStream);

        // Wait for either task to complete
        await Task.WhenAny(clientToRemote, remoteToClient);

        // Close the other stream and the remote socket
        if (!clientToRemote.IsCompleted) clientToRemote.ContinueWith(_ => clientStream.Close());

        if (!remoteToClient.IsCompleted) remoteToClient.ContinueWith(_ => remoteStream.Close());
    }
}