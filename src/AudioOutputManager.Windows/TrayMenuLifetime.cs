namespace AudioOutputManager.Windows;

/// <summary>UI-thread ownership of a pending tray command, independent of delayed WinUI callbacks.</summary>
public sealed class TrayMenuLifetime
{
    private Request? current;
    public Request? TryBegin()
    {
        if(current is not null)return null;
        return current=new Request();
    }
    public bool IsCurrent(Request request)=>ReferenceEquals(current,request);
    public void Complete(Request request,TrayMenuCommand command,Action? cleanup=null)
    {
        if(!IsCurrent(request))return;
        // Release ownership before hiding UI or completing an asynchronous waiter.
        current=null;
        try{cleanup?.Invoke();}finally{request.Completion.TrySetResult(command);}
    }
    public void Release(Request request,Action cleanup)
        =>Complete(request,TrayMenuCommand.None,cleanup);
    public sealed class Request
    {
        internal TaskCompletionSource<TrayMenuCommand> Completion {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TrayMenuCommand> Result=>Completion.Task;
    }
}
