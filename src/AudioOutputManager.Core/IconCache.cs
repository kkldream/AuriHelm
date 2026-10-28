namespace AudioOutputManager.Core;

public sealed record IconResult<T>(T? Value,bool Retry) where T:class;

/// <summary>Coalesces work across views; failures retry finitely and a new session renews the budget.</summary>
public sealed class IconCache<T>(Func<DateTimeOffset>? clock=null) where T:class
{
    private sealed class Entry
    {
        public string Revision="";
        public int Failures;
        public DateTimeOffset RetryAt;
        public Task<IconResult<T>>? Pending;
        public T? Value;
    }
    private readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate=new();
    public Task<IconResult<T>> GetAsync(string key,string revision,Func<Task<T?>> read)
    {
        TaskCompletionSource<IconResult<T>> completion;Entry entry;
        lock(gate)
        {
            if(!entries.TryGetValue(key,out entry!))
            {
                // Pending callers retain their own tasks; eviction never cancels another view.
                if(entries.Count>=128)entries.Remove(entries.Keys.First());
                entries[key]=entry=new();
            }
            if(entry.Value is not null)return Task.FromResult(new IconResult<T>(entry.Value,false));
            if(entry.Pending is not null)return entry.Pending;
            if(entry.Revision!=revision){entry.Revision=revision;entry.Failures=0;entry.RetryAt=default;}
            if(entry.Failures>=3)return Task.FromResult(new IconResult<T>(null,false));
            if((clock?.Invoke()??DateTimeOffset.UtcNow)<entry.RetryAt)return Task.FromResult(new IconResult<T>(null,true));
            completion=new(TaskCreationOptions.RunContinuationsAsynchronously);entry.Pending=completion.Task;
        }
        _=ReadAsync();return completion.Task;
        async Task ReadAsync()
        {
            T? value=null;
            try{value=await read().ConfigureAwait(false);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException){}
            catch(Exception ex){lock(gate)entry.Pending=null;completion.TrySetException(ex);return;}
            IconResult<T> result;
            lock(gate)
            {
                entry.Pending=null;entry.Value=value;
                if(value is null){entry.Failures++;entry.RetryAt=(clock?.Invoke()??DateTimeOffset.UtcNow).AddSeconds(5);}
                result=new(value,value is null&&entry.Failures<3);
            }
            completion.TrySetResult(result);
        }
    }
}

/// <summary>A late request cannot replace a newer view; a temporary failure keeps the same app's last image.</summary>
public sealed class LatestIcon<T> where T:class
{
    private long generation;
    private string? identity;
    public T? Value {get;private set;}
    public async Task<bool> UpdateAsync(string nextIdentity,Func<Task<T?>> read)
    {
        long current=++generation;
        if(identity!=nextIdentity){identity=nextIdentity;Value=null;}
        var next=await read();
        if(current!=generation)return false;
        if(next is not null)Value=next;
        return true;
    }
    public void Cancel()=>generation++;
}
