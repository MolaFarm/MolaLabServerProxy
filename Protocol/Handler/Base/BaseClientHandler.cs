using System.Net.Sockets;

namespace Protocol.Handler.Base;

/// <summary>
///     Represents an abstract base class for handling client connections.
/// </summary>
public abstract class BaseClientHandler
{
	/// <summary>
	///     Handles a client connection asynchronously.
	/// </summary>
	/// <param name="client">The connected client socket.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
	public abstract Task HandleClientAsync(Socket client);

	/// <summary>
	///     Pipes data between client and remote server streams.
	/// </summary>
	/// <param name="clientStream">The stream connected to the client.</param>
	/// <param name="remoteStream">The stream connected to the remote server.</param>
	/// <returns>A task representing the asynchronous operation.</returns>
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