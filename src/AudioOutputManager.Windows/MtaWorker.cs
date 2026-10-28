using System.Collections.Concurrent;

namespace AudioOutputManager.Windows;

/// <summary>All Core Audio COM objects live on this single MTA thread.</summary>
internal sealed class MtaWorker : IDisposable
{
    private readonly ConcurrentQueue<Action> queue=new(),meters=new();
    private readonly SemaphoreSlim available=new(0);
    private readonly object gate=new();
    private bool accepting=true;
    private readonly Thread thread;
    public MtaWorker()
    {
        thread = new Thread(Run) { IsBackground=true, Name="Audio Core MTA" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    private void Run()
    {
        while(true)
        {
            available.Wait();
            if(queue.TryDequeue(out var action)||meters.TryDequeue(out action))action();
            else {lock(gate)if(!accepting)return;}
        }
    }
    public Task<T> Invoke<T>(Func<T> action)=>Enqueue(action,false);
    public Task<T> InvokeMeter<T>(Func<T> action)=>Enqueue(action,true);
    private Task<T> Enqueue<T>(Func<T> action,bool meter)
    {
        var done=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock(gate)
        {
            if(!accepting){done.SetException(new ObjectDisposedException(nameof(MtaWorker)));return done.Task;}
            (meter?meters:queue).Enqueue(()=>{try{done.SetResult(action());}catch(Exception ex){done.SetException(ex);}});available.Release();
        }
        return done.Task;
    }
    public Task Invoke(Action action) => Invoke(() => { action(); return true; });
    public void Dispose(){lock(gate){if(!accepting)return;accepting=false;available.Release();}if(Thread.CurrentThread!=thread){thread.Join();available.Dispose();}}
}
