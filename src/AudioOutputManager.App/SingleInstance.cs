using System.IO.Pipes;

namespace AudioOutputManager.App;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource stop=new();
    private readonly string name="AudioOutputManager."+Environment.UserName;
    private Task? listener;
    public bool IsPrimary {get;}
    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public SingleInstance()
    {
        mutex=new Mutex(true,"Local\\"+name,out bool created);IsPrimary=created;
    }
    public async Task SignalAsync(byte command=1)
    {
        using var pipe=new NamedPipeClientStream(".",name,PipeDirection.Out,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000);await pipe.WriteAsync(new byte[]{command});
    }
    public async Task ListenAsync()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                var b=new byte[1];if(await pipe.ReadAsync(b,stop.Token)>0){if(b[0]==2)ExitRequested?.Invoke();else OpenRequested?.Invoke();}
            }
            catch(OperationCanceledException) { break; }
            catch(IOException) { }
        }
    }
    public void StartListener()=>listener=Task.Run(ListenAsync);
    public void Dispose() { stop.Cancel();listener?.GetAwaiter().GetResult();if(IsPrimary)mutex.ReleaseMutex();mutex.Dispose();stop.Dispose(); }
}
