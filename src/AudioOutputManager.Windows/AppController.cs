using System.Diagnostics;
using AudioOutputManager.Core;

namespace AudioOutputManager.Windows;

public sealed class AppController : IDisposable
{
    public IAudioService Audio {get;}
    private readonly SettingsStore store;
    private readonly AutomaticPolicy policy=new();
    private readonly ReconnectionTracker reconnections=new();
    private readonly IHeadsetProvider headset;
    private readonly SemaphoreSlim operations=new(1,1);
    private readonly CancellationTokenSource stop=new();
    private readonly SwitchCoordinator switches;
    private readonly object roleLock=new();
    private Dictionary<AudioRole,string?> observedRoles=[];
    private Task? monitor;
    private int refreshing;
    private int pendingDefaultChanges;
    private readonly object lifetime=new();
    private readonly HashSet<Task> callbacks=[];
    private bool disposing;
    private volatile bool suspended;
    private CancellationTokenSource? currentQuery;
    private long powerGeneration;
    private string? lastAutomaticTarget;
    public AppSettings Settings {get;private set;}=new();
    public AudioSnapshot Snapshot {get;private set;}=new([],new Dictionary<AudioRole,string?>(),DateTimeOffset.UtcNow);
    public HeadsetReading Headset {get;private set;}=new(HeadsetState.Unknown,DateTimeOffset.MinValue);
    public string Status {get;private set;}="正在初始化";
    public string? HeadsetId
    {
        get
        {
            if(Headset.ContainerId is null)return null;
            var matches=Snapshot.Outputs.Where(d=>d.ContainerId==Headset.ContainerId).ToArray();
            return matches.Length==1?matches[0].Id:null;
        }
    }
    public event Action? Updated;
    public event Action<string>? Issue;
    public event Action<string>? Diagnostic;
    public AppController(string directory) : this(directory,new CoreAudioService(),new ProX2Headset()) { }
    public AppController(string directory,IAudioService audio,IHeadsetProvider headset)
    {
        Audio=audio;this.headset=headset;
        store=new(directory); switches=new(Audio);
        Audio.Changed+=Refresh;
        Audio.DefaultChanged+=OnDefault;
    }
    public async Task StartAsync()
    {
        var loaded=store.Load(); Settings=loaded.Settings;
        await Audio.InitializeAsync(); Snapshot=await Audio.ReadAsync();
        reconnections.Observe(Snapshot);
        lock(roleLock) observedRoles=Snapshot.Defaults.ToDictionary();
        Status=loaded.Warning??(Settings.Automatic?"等待裝置狀態":"已關閉");
        monitor=Task.Run(()=>MonitorAsync(stop.Token)); Updated?.Invoke();
    }
    private async Task MonitorAsync(CancellationToken cancellation)
    {
        int failures=0;
        while(!cancellation.IsCancellationRequested)
        {
            long started=Stopwatch.GetTimestamp();
            try
            {
                if(suspended){await Task.Delay(500,cancellation);continue;}
                long epoch=Interlocked.Read(ref powerGeneration);
                using var query=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                currentQuery=query;
                var reading=await headset.QueryAsync(query.Token);
                currentQuery=null;
                await operations.WaitAsync(cancellation);
                try
                {
                    if(suspended||epoch!=Interlocked.Read(ref powerGeneration)){policy.Reset();continue;}
                    long decisionGeneration=switches.Generation;
                    UpdateSnapshot(await Audio.ReadAsync());
                    Headset=reading; failures=reading.State==HeadsetState.Unknown?Math.Min(failures+1,4):0;
                    policy.Observe(reading,DateTimeOffset.UtcNow,Settings.OffConfirmationSeconds);
                    if(lastAutomaticTarget is not null&&!Snapshot.Outputs.Any(d=>d.Id==lastAutomaticTarget&&d.Available))lastAutomaticTarget=null;
                    if(Snapshot.Outputs.Any(d=>!Settings.KnownDevices.ContainsKey(d.Id)))
                    {
                        var known=new Dictionary<string,string>(Settings.KnownDevices);foreach(var d in Snapshot.Outputs)known[d.Id]=d.Name;
                        var next=Settings with{KnownDevices=known};store.Save(next);Settings=next;
                    }
                    if(Settings.Automatic)
                    {
                        Dictionary<AudioRole,string?> previousRoles;
                        lock(roleLock) { previousRoles=observedRoles.ToDictionary();observedRoles=Snapshot.Defaults.ToDictionary(); }
                        // Notifications can lag behind the actual default. Respect a choice seen by readback
                        // before issuing another automatic command, unless its old endpoint disconnected.
                        bool external=false;
                        foreach(var role in Snapshot.Defaults)
                        {
                            string? previous=previousRoles.GetValueOrDefault(role.Key);
                            if(previous==role.Value)continue;
                            if(reconnections.IsExpectedReturn(role.Value,Settings.Priority)){lastAutomaticTarget=null;continue;}
                            if(previous is not null&&Snapshot.Outputs.Any(d=>d.Id==previous&&d.Available))external=true;
                        }
                        // Pending notifications classify their own latest readback after this gate.
                        // Their provisional role cache must not be mistaken for another user choice.
                        if(external&&Volatile.Read(ref pendingDefaultChanges)==0)
                        {
                            switches.Invalidate();Settings=Settings with{Automatic=false};
                            reconnections.ClearPending();
                            Status="偵測到外部輸出變更，自動切換已關閉";Diagnostic?.Invoke("external-default-change");
                            try{store.Save(Settings);}catch(Exception ex){Report("未能儲存自動關閉狀態："+ex.HResult.ToString("X8"));}
                        }
                    }
                    if(Settings.Automatic&&Volatile.Read(ref pendingDefaultChanges)==0)
                    {
                        if(HeadsetId is null&&Snapshot.Outputs.Any(d=>d.IsProX2&&d.Available&&Settings.Priority.Contains(d.Id)))Status="等待可靠的耳機端點對應";
                        else
                        {
                            string? target=policy.Choose(Settings,Snapshot,HeadsetId,DateTimeOffset.UtcNow-Headset.ObservedAt<=TimeSpan.FromSeconds(2));
                            if(target is not null && target!=lastAutomaticTarget) await SwitchCore(target,cancellation,decisionGeneration);
                            else Status=Settings.Priority.Length==0?"等待設定優先序":!Settings.Priority.Any(id=>Snapshot.Outputs.Any(d=>d.Id==id&&d.Available))?"優先序內沒有可用裝置":HeadsetId is not null&&Settings.Priority.Contains(HeadsetId)&&(policy.Confirmed==HeadsetState.Unknown||DateTimeOffset.UtcNow-Headset.ObservedAt>TimeSpan.FromSeconds(2))?"等待裝置狀態":"運作中";
                            reconnections.Settle(Snapshot,HeadsetId,policy.Confirmed,DateTimeOffset.UtcNow-Headset.ObservedAt<=TimeSpan.FromSeconds(2));
                        }
                    }
                }
                finally { operations.Release(); }
                Updated?.Invoke();
            }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { break; }
            catch(Exception ex) { Report("監測失敗："+ex.HResult.ToString("X8")); }
            var delay=TimeSpan.FromMilliseconds(Math.Min(5000,500*(1<<failures)))-Stopwatch.GetElapsedTime(started);
            try { await Task.Delay(delay>TimeSpan.Zero?delay:TimeSpan.FromMilliseconds(10),cancellation); } catch(OperationCanceledException) { break; }
        }
    }
    public async Task PowerChangedAsync(bool sleeping)
    {
        suspended=sleeping;Interlocked.Increment(ref powerGeneration);switches.Invalidate();Diagnostic?.Invoke(sleeping?"suspend":"resume");
        try{currentQuery?.Cancel();}catch(ObjectDisposedException){}
        await operations.WaitAsync();
        try{policy.Reset();lastAutomaticTarget=null;reconnections.ClearPending();Headset=new(HeadsetState.Unknown,DateTimeOffset.UtcNow);Status=sleeping?"系統休眠中":"等待喚醒後的裝置狀態";}
        finally{operations.Release();Updated?.Invoke();}
    }
    private void OnDefault(AudioRole role,string? id)
    {
        lock(lifetime)
        {
        if(disposing)return;
        if(switches.AcceptNotification(role,id)) { lock(roleLock) observedRoles[role]=id; return; }
        string? previous;
        lock(roleLock) { previous=observedRoles.GetValueOrDefault(role); observedRoles[role]=id; }
        if(previous==id) return;
        Interlocked.Increment(ref pendingDefaultChanges);
        switches.Invalidate();
        ScheduleCallback(()=>HandleExternalAsync(role,previous,id));
        }
    }
    private async Task HandleExternalAsync(AudioRole role,string? previous,string? current)
    {
        try
        {
            await operations.WaitAsync(stop.Token);
            try
            {
                UpdateSnapshot(await Audio.ReadAsync());
                // A queued removal notification can outlive a complete USB reconnect.
                // Classify only the default that is still current for this role; otherwise
                // a now-available old endpoint would turn that stale event into a manual choice.
                string? latest=Snapshot.Defaults.GetValueOrDefault(role);
                if(latest!=current)
                {
                    // Do not overwrite a newer notification that arrived during the read.
                    lock(roleLock)if(observedRoles.GetValueOrDefault(role)==current)observedRoles[role]=latest;
                }
                else
                {
                    // Availability and defaults must describe the same observation. A new
                    // availability query can already see a replug after the removal snapshot.
                    bool disconnected=previous is not null&&!Snapshot.Outputs.Any(d=>d.Id==previous&&d.Available);
                    bool returning=reconnections.IsExpectedReturn(current,Settings.Priority);
                    if(disconnected||returning)lastAutomaticTarget=null;
                    if(returning)Diagnostic?.Invoke("device-return-default");
                    if(!disconnected&&!returning&&Settings.Automatic)
                    {
                        Settings=Settings with { Automatic=false }; // Stop even when saving fails.
                        reconnections.ClearPending();
                        Status="偵測到外部輸出變更，自動切換已關閉";
                        Diagnostic?.Invoke("external-default-change");
                        try { store.Save(Settings); } catch(Exception ex) { Report("未能儲存自動關閉狀態："+ex.HResult.ToString("X8")); }
                    }
                }
            }
            finally { operations.Release(); }
            Updated?.Invoke();
        }
        catch(OperationCanceledException) { }
        catch(Exception ex) { Report("外部輸出狀態讀取失敗："+ex.HResult.ToString("X8")); }
        finally { Interlocked.Decrement(ref pendingDefaultChanges); }
    }
    private void Refresh()
    {
        lock(lifetime)
        {
        if(disposing)return;
        if(stop.IsCancellationRequested||Interlocked.Exchange(ref refreshing,1)!=0) return;
        ScheduleCallback(RefreshAsync);
        }
    }
    // Called with lifetime held. Dispatch off the UI context so synchronous Dispose can drain safely.
    private void ScheduleCallback(Func<Task> callback)
    {
        var task=Task.Run(callback);callbacks.Add(task);
        _=task.ContinueWith(completed=>{lock(lifetime)callbacks.Remove(completed);},TaskScheduler.Default);
    }
    private async Task RefreshAsync()
    {
        try
        {
            await operations.WaitAsync(stop.Token);
            try{UpdateSnapshot(await Audio.ReadAsync());}
            finally{operations.Release();}
            Updated?.Invoke();
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception ex) { Report("音訊狀態讀取失敗："+ex.HResult.ToString("X8")); }
        finally { Interlocked.Exchange(ref refreshing,0); }
    }
    private void UpdateSnapshot(AudioSnapshot snapshot)
    {
        if(reconnections.Observe(snapshot))
        {
            policy.Reset();lastAutomaticTarget=null;
            Headset=new(HeadsetState.Unknown,DateTimeOffset.UtcNow);
        }
        Snapshot=snapshot;
    }
    private void Report(string message) { Status=message; Issue?.Invoke(message); Updated?.Invoke(); }
    private async Task SwitchCore(string id,CancellationToken cancellation,long? decisionGeneration=null)
    {
        if(decisionGeneration.HasValue&&Volatile.Read(ref pendingDefaultChanges)>0)return;
        var result=await switches.SwitchAsync(id,cancellation,decisionGeneration);
        Diagnostic?.Invoke(result.Success?"switch-readback-success":result.Interrupted?"switch-superseded":result.TargetUnavailable?"switch-target-unavailable":"switch-failed");
        Snapshot=await Audio.ReadAsync();
        lock(roleLock) observedRoles=Snapshot.Defaults.ToDictionary();
        if(result.Success&&Settings.Automatic)lastAutomaticTarget=id;
        Status=result.Message;
        if(!result.Success&&!result.Interrupted&&!result.TargetUnavailable)
        {
            Settings=Settings with {Automatic=false};
            reconnections.ClearPending();
            try { store.Save(Settings); } catch(Exception ex) { Report("未能儲存："+ex.HResult.ToString("X8")); }
        }
    }
    public async Task ManualSwitchAsync(string id)
    {
        switches.Invalidate(); await operations.WaitAsync();
        try
        {
            Settings=Settings with {Automatic=false};
            reconnections.ClearPending();
            try { store.Save(Settings); } catch(Exception ex) { Report("未能儲存："+ex.HResult.ToString("X8")); }
            await SwitchCore(id,stop.Token);
            if(Snapshot.RolesConsistent) Status="因手動選擇而關閉";
        }
        finally { operations.Release(); Updated?.Invoke(); }
    }
    public async Task SetAutomaticAsync(bool enabled)
    {
        switches.Invalidate(); await operations.WaitAsync();
        try
        {
            var next=Settings with {Automatic=enabled};
            reconnections.ClearPending();
            if(!enabled) Settings=next;
            store.Save(next); Settings=next; policy.Reset();lastAutomaticTarget=null;
            Status=enabled?"等待裝置狀態":"已關閉";
        }
        finally { operations.Release(); Updated?.Invoke(); }
    }
    public async Task SaveAsync(AppSettings next)
        =>await SaveAsync(_=>next);
    public async Task SaveAsync(Func<AppSettings,AppSettings> update)
    {
        await operations.WaitAsync();
        try
        {
            var next=update(Settings);
            bool policyChanged=next.Automatic!=Settings.Automatic||next.OffConfirmationSeconds!=Settings.OffConfirmationSeconds||!next.Priority.SequenceEqual(Settings.Priority);
            if(policyChanged)switches.Invalidate();
            store.Save(next); Settings=next; if(policyChanged){policy.Reset();lastAutomaticTarget=null;reconnections.ClearPending();}
        }
        finally { operations.Release(); Updated?.Invoke(); }
    }
    public async Task NextAsync()
    {
        var available=Snapshot.Outputs.Where(d=>d.Available&&(!(d.IsProX2||d.Id==HeadsetId)||(d.Id==HeadsetId&&Headset.State==HeadsetState.On&&policy.Confirmed==HeadsetState.On&&DateTimeOffset.UtcNow-Headset.ObservedAt<=TimeSpan.FromSeconds(2)))).Select(d=>d.Id);
        string? next=AutomaticPolicy.Next(Settings.Priority,available,Snapshot.CurrentId);
        if(next is not null) await ManualSwitchAsync(next);
        else Report("優先序內沒有其他可用輸出。");
    }
    public void Dispose()
    {
        Task[] pending;
        lock(lifetime)
        {
            if(disposing)return;disposing=true;
            Audio.Changed-=Refresh;Audio.DefaultChanged-=OnDefault;pending=callbacks.ToArray();
        }
        stop.Cancel(); switches.Invalidate();
        try
        {
            try{monitor?.GetAwaiter().GetResult();}catch(OperationCanceledException){}
            Task.WhenAll(pending).GetAwaiter().GetResult();
        }
        finally{Audio.Dispose();stop.Dispose();}
    }
}
