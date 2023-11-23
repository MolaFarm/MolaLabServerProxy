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

        Dispatcher.UIThread.Invoke(() => {
            var frame = new DispatcherFrame();
            task.ContinueWith(t => { frame.Continue = false; });
            Dispatcher.UIThread.PushFrame(frame);
        });

        return task.Result;
    }
}