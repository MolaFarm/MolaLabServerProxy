using System;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ServerProxy.Tools;

internal class Awaiter
{
    public static TResult AwaitByPushFrame<TResult>(Task<TResult> task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        Contract.EndContractBlock();

        var frame = new DispatcherFrame();
        task.ContinueWith(t => { frame.Continue = false; });
        Dispatcher.UIThread.PushFrame(frame);
        return task.Result;
    }

    public static void AwaitByPushFrame(Task task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        Contract.EndContractBlock();

        var frame = new DispatcherFrame();
        task.ContinueWith(t => { frame.Continue = false; });
        Dispatcher.UIThread.PushFrame(frame);
        Task.WaitAny(task);
    }
}