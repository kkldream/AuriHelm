using System.Diagnostics;
using AudioOutputManager.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioOutputManager.Windows;

public sealed class CoreAudioService : IAudioService,ILiveAudioService
{
    private readonly MtaWorker worker=new();
    private MMDeviceEnumerator? enumerator;
    private readonly Dictionary<string,DeviceEntry> devices=[];
    private readonly AudioNotifications notifications;
    private int refreshPending;
    private readonly object sessionGate=new();
    private readonly Dictionary<string,long> pendingSessions=[];
    private volatile bool disposed;
    private WasapiOut? testTone;
    private CancellationTokenSource? testStop;
    public event Action? Changed;
    public event Action<AudioRole,string?>? DefaultChanged;
    public event Action<AudioSnapshot>? ValuesChanged;
    private IReadOnlyDictionary<AudioRole,string?> roles=new Dictionary<AudioRole,string?>();
    private long topologyReads,snapshotReads,peakReads,peakTargets;
    public (long TopologyReads,long SnapshotReads,long PeakReads,long PeakTargets) Counters =>
        (Interlocked.Read(ref topologyReads),Interlocked.Read(ref snapshotReads),Interlocked.Read(ref peakReads),Interlocked.Read(ref peakTargets));

    public CoreAudioService() { notifications=new(RequestRefresh,RequestMetadata,RequestDefaultRefresh,(role,id)=>DefaultChanged?.Invoke(role,id)); }
    public Task InitializeAsync() => worker.Invoke(() =>
    {
        enumerator=new(); enumerator.RegisterEndpointNotificationCallback(notifications); Rebuild();
    });
    private void RequestRefresh()
    {
        if(disposed || Interlocked.Exchange(ref refreshPending,1)!=0) return;
        _=worker.Invoke(() => { try { if(!disposed) Rebuild(); } finally { Interlocked.Exchange(ref refreshPending,0); } }).ContinueWith(_=>Changed?.Invoke(),TaskScheduler.Default);
    }
    private void RequestDefaultRefresh(string? id)
    {
        if(disposed)return;
        _=worker.Invoke(()=>
        {
            if(disposed)return;
            // Changing a role does not invalidate every endpoint's properties or sessions.
            // A newly attached default can precede its device-added notification, so discover it
            // when absent; device/session notifications retain their normal rebuild paths.
            if(id is not null&&!devices.ContainsKey(id))Rebuild();
            else roles=ReadRoles();
        }).ContinueWith(t=>{if(t.IsCompletedSuccessfully&&!disposed)Changed?.Invoke();},TaskScheduler.Default);
    }
    private void RequestMetadata(string id)
    {
        if(disposed)return;
        _=worker.Invoke(()=>
        {
            if(disposed)return;
            if(devices.TryGetValue(id,out var device))device.RefreshMetadata();
            else Rebuild();
        }).ContinueWith(t=>{if(t.IsCompletedSuccessfully&&!disposed)Changed?.Invoke();},TaskScheduler.Default);
    }
    private void Rebuild()
    {
        if(enumerator is null) return;
        Interlocked.Increment(ref topologyReads);
        var live=new HashSet<string>();
        foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
        {
            string id=device.ID; live.Add(id);
            if(devices.ContainsKey(id)) { device.Dispose(); continue; }
            try
            {
                var entry=new DeviceEntry(device,()=>RequestSessions(id),RequestValues);
                devices.Add(id,entry);
            }
            catch { device.Dispose(); }
        }
        foreach(string missing in devices.Keys.Where(x=>!live.Contains(x)).ToArray()) { devices[missing].Dispose(); devices.Remove(missing); }
        foreach(var entry in devices.Values){entry.RefreshMetadata();entry.RefreshSessions();}
        roles=ReadRoles();
    }
    private void RequestSessions(string id)
    {
        lock(sessionGate)
        {
            if(disposed)return;
            if(pendingSessions.TryGetValue(id,out long generation)){pendingSessions[id]=generation+1;return;}
            pendingSessions[id]=1;
        }
        QueueSessions(id);
    }
    private void QueueSessions(string id)
    {
        // Session events do not invalidate other devices' names, properties or session collections.
        // Requeue an event received during the COM read, yielding to queued controls between reads.
        _=worker.Invoke(()=>
        {
            long observed;lock(sessionGate)observed=pendingSessions[id];
            try{if(!disposed&&devices.TryGetValue(id,out var device))device.RefreshSessions();}
            catch{RequestRefresh();throw;}
            finally
            {
                bool again;
                lock(sessionGate){again=!disposed&&pendingSessions[id]!=observed;if(!again)pendingSessions.Remove(id);}
                if(again)QueueSessions(id);
            }
        }).ContinueWith(t=>{if(t.IsCompletedSuccessfully&&!disposed)Changed?.Invoke();},TaskScheduler.Default);
    }
    private void RequestValues(Action update)
    {
        if(disposed)return;
        _=worker.Invoke(()=>
        {
            if(disposed)return null;
            try{update();return Snapshot();}
            catch(Exception ex) when(ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException){RequestRefresh();return null;}
        }).ContinueWith(t=>
        {
            if(t.IsCompletedSuccessfully&&t.Result is {} snapshot&&!disposed){ValuesChanged?.Invoke(snapshot);Changed?.Invoke();}
        },TaskScheduler.Default);
    }
    public Task<AudioSnapshot> ReadAsync(bool peaks=false)=>worker.Invoke(()=>
    {
        roles=ReadRoles();Interlocked.Increment(ref snapshotReads);
        // Controller classification needs availability from this worker turn too,
        // even when a coalesced topology refresh has not rebuilt the device cache yet.
        foreach(var entry in devices.Values)entry.RefreshAvailability();
        return Snapshot(peaks?devices.Values.Select(d=>new PeakTarget(d.Device.ID,d.Sessions.Keys.ToHashSet(),true)).ToArray():null);
    });
    public Task<AudioSnapshot> ReadPeaksAsync(IReadOnlyList<PeakTarget> visible)=>worker.InvokeMeter(()=>Snapshot(visible));
    public Task<AudioSnapshot> ReadValuesAsync(string deviceId,IReadOnlySet<string>? sessions=null)=>worker.Invoke(()=>
    {
        var device=devices[deviceId];
        if(sessions is null)device.RefreshValues();
        else
        {
            foreach(string id in sessions)if(device.Sessions.TryGetValue(id,out var session))session.RefreshValues();
        }
        return Snapshot();
    });
    private AudioSnapshot Snapshot(IReadOnlyList<PeakTarget>? visible=null)
    {
        if(visible is not null)Interlocked.Increment(ref peakReads);
        var outputs=new List<AudioOutput>();
        string? error=null;
        foreach(var entry in devices.Values)
        {
            try
            {
                var target=visible?.FirstOrDefault(t=>t.DeviceId==entry.Value.Id);
                var sessions=new List<AudioSession>();
                foreach(var session in entry.Sessions.Values)
                {
                    try
                    {
                        float? peak=null;
                        if(target?.SessionIds.Contains(session.Value.Id)==true){Interlocked.Increment(ref peakTargets);peak=session.Control.AudioMeterInformation.MasterPeakValue;}
                        sessions.Add(session.Value with{Peak=peak});
                    }
                    catch(System.Runtime.InteropServices.COMException) { }
                }
                float? endpointPeak=null;
                if(target?.Endpoint==true){Interlocked.Increment(ref peakTargets);endpointPeak=entry.Device.AudioMeterInformation.MasterPeakValue;}
                outputs.Add(entry.Value with{Peak=endpointPeak,Sessions=sessions.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Id).ToArray()});
            }
            catch(Exception ex) when(ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { error="部分裝置無法讀取："+ex.HResult.ToString("X8"); }
        }
        return new AudioSnapshot(outputs,roles,DateTimeOffset.UtcNow,error);
    }
    private IReadOnlyDictionary<AudioRole,string?> ReadRoles()
    {
        Dictionary<AudioRole,string?> result=[];
        foreach(var role in Enum.GetValues<AudioRole>())
        {
            try { using var d=enumerator!.GetDefaultAudioEndpoint(DataFlow.Render,(Role)role); result[role]=d.ID; }
            catch(System.Runtime.InteropServices.COMException) { result[role]=null; }
        }
        return result;
    }
    public Task<IReadOnlyDictionary<AudioRole,string?>> ReadRolesAsync()=>worker.Invoke(ReadRoles);
    public Task<bool> IsAvailableAsync(string id)=>worker.Invoke(()=>devices.TryGetValue(id,out var d)&&d.Device.State==DeviceState.Active);
    public Task SetRoleAsync(AudioRole role,string id,Func<bool> stillCurrent)=>worker.Invoke(()=>{if(!stillCurrent())throw new OperationCanceledException();DefaultOutputInterop.Set(role,id);});
    public Task SetEndpointVolumeAsync(string id,float volume)=>worker.Invoke(()=>{var d=devices[id];d.Device.AudioEndpointVolume.MasterVolumeLevelScalar=Math.Clamp(volume,0,1);d.RefreshValues();});
    public Task SetEndpointMuteAsync(string id,bool muted)=>worker.Invoke(()=>{var d=devices[id];d.Device.AudioEndpointVolume.Mute=muted;d.RefreshValues();});
    public Task SetSessionVolumeAsync(string deviceId,IEnumerable<string> ids,float volume)=>worker.Invoke(()=>
    {
        int count=0;
        foreach(string id in ids) if(devices[deviceId].Sessions.TryGetValue(id,out var session)){session.Control.SimpleAudioVolume.Volume=Math.Clamp(volume,0,1);session.RefreshValues();count++;}
        if(count==0)throw new InvalidOperationException("音訊工作階段已結束，請重新選擇。");
    });
    public Task SetSessionMuteAsync(string deviceId,IEnumerable<string> ids,bool muted)=>worker.Invoke(()=>
    {
        int count=0;
        foreach(string id in ids) if(devices[deviceId].Sessions.TryGetValue(id,out var session)){session.Control.SimpleAudioVolume.Mute=muted;session.RefreshValues();count++;}
        if(count==0)throw new InvalidOperationException("音訊工作階段已結束，請重新選擇。");
    });
    public Task PlayTestToneAsync(string id)=>worker.Invoke(()=>
    {
        StopTone();
        var device=devices[id].Device;
        if(device.AudioEndpointVolume.Mute)throw new InvalidOperationException("裝置已靜音，未播放測試音。");
        var signal=new SignalGenerator(48000,2){Gain=.02,Frequency=440,Type=SignalGeneratorType.Sin};
        testTone=new WasapiOut(device,AudioClientShareMode.Shared,true,100);
        testTone.Init(new SampleToWaveProvider(signal.Take(TimeSpan.FromSeconds(1))));testTone.Play();
        var cancellation=testStop=new();
        _=StopAfterDelay(cancellation.Token);
    });
    private async Task StopAfterDelay(CancellationToken token)
    {
        try{await Task.Delay(1000,token);if(!disposed)await worker.Invoke(StopTone);}catch(OperationCanceledException){}catch(ObjectDisposedException){}
    }
    private void StopTone(){testStop?.Cancel();testStop?.Dispose();testStop=null;testTone?.Stop();testTone?.Dispose();testTone=null;}
    public Task StopTestToneAsync()=>worker.Invoke(StopTone);
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        try
        {
            worker.Invoke(()=>
            {
                void Cleanup(Action action){try{action();}catch(System.Runtime.InteropServices.COMException){}catch(InvalidOperationException){}}
                Cleanup(StopTone);foreach(var d in devices.Values)Cleanup(d.Dispose);devices.Clear();
                if(enumerator is not null){Cleanup(()=>enumerator.UnregisterEndpointNotificationCallback(notifications));Cleanup(enumerator.Dispose);}
            }).GetAwaiter().GetResult();
        }
        finally{worker.Dispose();}
    }
    private sealed class DeviceEntry : IDisposable
    {
        public MMDevice Device {get;}
        public Dictionary<string,SessionEntry> Sessions {get;}=[];
        private readonly Action changed;
        private readonly Action<Action> valuesChanged;
        public AudioOutput Value {get;private set;}=null!;
        public void RefreshAvailability()=>Value=Value with{Available=Device.State==DeviceState.Active};
        public DeviceEntry(MMDevice device,Action changed,Action<Action> valuesChanged)
        {
            Device=device; this.changed=changed;this.valuesChanged=valuesChanged;
            RefreshMetadata();
            Device.AudioEndpointVolume.OnVolumeNotification += VolumeChanged;
            Device.AudioSessionManager.OnSessionCreated += SessionCreated;
            _=Device.AudioSessionManager.Sessions.Count;
        }
        // Read just this target's current value on the owning MTA, so delayed notifications cannot regress it.
        private void VolumeChanged(AudioVolumeNotificationData data)=>valuesChanged(RefreshValues);
        private void SessionCreated(object sender,IAudioSessionControl control)=>changed();
        public void RefreshValues()=>Value=Value with{Volume=Device.AudioEndpointVolume.MasterVolumeLevelScalar,Muted=Device.AudioEndpointVolume.Mute};
        public void RefreshMetadata()
        {
            var property=Device.Properties[AudioNotifications.ContainerId];
            Guid? container=Guid.TryParse(property?.Value?.ToString(),out var parsed)?parsed:null;
            Value=new(Device.ID,Device.FriendlyName,true,Device.AudioEndpointVolume.MasterVolumeLevelScalar,Device.AudioEndpointVolume.Mute,null,[],container,Device.DeviceFriendlyName.Contains("PRO X 2",StringComparison.OrdinalIgnoreCase));
        }
        public void RefreshSessions()
        {
            Device.AudioSessionManager.RefreshSessions();
            var collection=Device.AudioSessionManager.Sessions;
            var live=new HashSet<string>();
            for(int i=0;i<collection.Count;i++)
            {
                var control=collection[i];
                try
                {
                    if(control.GetProcessID==(uint)Environment.ProcessId){control.Dispose();continue;}
                    if(control.State==AudioSessionState.AudioSessionStateExpired){control.Dispose();continue;}
                    string id=control.GetSessionInstanceIdentifier;
                    live.Add(id);
                    if(Sessions.TryGetValue(id,out var existing)) { control.Dispose();existing.RefreshMetadata();continue; }
                    Sessions[id]=new(control,changed,valuesChanged);
                }
                catch { control.Dispose(); }
            }
            foreach(var missing in Sessions.Keys.Where(x=>!live.Contains(x)).ToArray()) { Sessions[missing].Dispose(); Sessions.Remove(missing); }
        }
        public void Dispose()
        {
            Device.AudioEndpointVolume.OnVolumeNotification-=VolumeChanged;
            Device.AudioSessionManager.OnSessionCreated-=SessionCreated;
            foreach(var s in Sessions.Values) s.Dispose(); Device.Dispose();
        }
    }
    private sealed class SessionEntry : IAudioSessionEventsHandler, IDisposable
    {
        private static long iconSequence;
        private long iconRevision;
        public AudioSessionControl Control {get;}
        private readonly string fallbackName;
        public AudioSession Value {get;private set;}=null!;
        public string Identity {get;}
        public string? IconFile {get;}
        private readonly Action changed;
        private readonly Action<Action> valuesChanged;
        public SessionEntry(AudioSessionControl control,Action changed,Action<Action> valuesChanged)
        {
            Control=control; this.changed=changed;this.valuesChanged=valuesChanged;
            fallbackName=control.IsSystemSoundsSession?"系統音效":control.DisplayName;
            Identity=control.IsSystemSoundsSession?AppExclusions.SystemSounds:"session:"+control.GetSessionInstanceIdentifier;
            try
            {
                if(control.IsSystemSoundsSession) { RefreshMetadata();control.RegisterEventClient(this);return; }
                using var process=Process.GetProcessById((int)control.GetProcessID);
                if(string.IsNullOrWhiteSpace(fallbackName)) fallbackName=process.ProcessName;
                string? path=process.MainModule?.FileName;
                IconFile=path;
                uint length=0;
                int result=GetApplicationUserModelId(process.Handle,ref length,null);
                if(result==122&&length is >0 and <2048)
                {
                    var appId=new System.Text.StringBuilder((int)length);
                    if(GetApplicationUserModelId(process.Handle,ref length,appId)==0)Identity="aumid:"+appId;
                }
                else if(path is not null&&!string.Equals(process.ProcessName,"ApplicationFrameHost",StringComparison.OrdinalIgnoreCase)&&!string.Equals(process.ProcessName,"WWAHost",StringComparison.OrdinalIgnoreCase))Identity="exe:"+path;
            }
            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if(string.IsNullOrWhiteSpace(fallbackName)) fallbackName="音訊工作階段";
            RefreshMetadata();
            control.RegisterEventClient(this);
        }
        public void RefreshMetadata()
        {
            string name=Control.IsSystemSoundsSession||string.IsNullOrWhiteSpace(Control.DisplayName)?fallbackName:Control.DisplayName;
            string? iconPath=null;
            try{iconPath=Control.IconPath;}catch(System.Runtime.InteropServices.COMException){}
            Value=new(Control.GetSessionInstanceIdentifier,name,Identity,Control.GetGroupingParam(),Control.SimpleAudioVolume.Volume,Control.SimpleAudioVolume.Mute,null,IconFile,iconPath,Interlocked.Read(ref iconRevision));
        }
        public void RefreshValues()=>Value=Value with{Volume=Control.SimpleAudioVolume.Volume,Muted=Control.SimpleAudioVolume.Mute};
        public void Dispose() { Control.UnRegisterEventClient(this); Control.Dispose(); }
        public void OnVolumeChanged(float volume,bool isMuted)=>valuesChanged(RefreshValues);
        public void OnDisplayNameChanged(string displayName)=>changed();
        public void OnIconPathChanged(string iconPath){Interlocked.Exchange(ref iconRevision,Interlocked.Increment(ref iconSequence));changed();}
        public void OnChannelVolumeChanged(uint channelCount,IntPtr newVolumes,uint channelIndex)=>valuesChanged(RefreshValues);
        public void OnGroupingParamChanged(ref Guid groupingId)=>changed();
        public void OnStateChanged(AudioSessionState state)=>changed();
        public void OnSessionDisconnected(AudioSessionDisconnectReason reason)=>changed();
        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(nint process,ref uint length,System.Text.StringBuilder? id);
    }
}
