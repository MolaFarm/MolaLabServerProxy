using System;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ServerProxy.Tools;

/// <summary>
///     Provides a utility class for asynchronous operations.
/// </summary>
internal class Awaiter
{
	/// <summary>
	///     Waits for the completion of a Task by using a Dispatcher frame.
	/// </summary>
	/// <typeparam name="TResult">The type of the result produced by the Task.</typeparam>
	/// <param name="task">The Task to await.</param>
	/// <returns>The result of the completed Task.</returns>
	public static TResult AwaitByPushFrame<TResult>(Task<TResult> task)
	{
		// Check for a null task and throw an ArgumentNullException if null.
		ArgumentNullException.ThrowIfNull(task);
		Contract.EndContractBlock();

		// Invoke the UI thread to use the Dispatcher.
		Dispatcher.UIThread.Invoke(() =>
		{
			// Create a new DispatcherFrame.
			var frame = new DispatcherFrame();

			// Continue the frame when the task completes.
			task.ContinueWith(t => { frame.Continue = false; });

			// Push the frame onto the Dispatcher queue.
			Dispatcher.UIThread.PushFrame(frame);
		});

		// Return the result of the completed task.
		return task.Result;
	}
}