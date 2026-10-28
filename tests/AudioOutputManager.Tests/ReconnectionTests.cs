using AudioOutputManager.Core;
using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class ReconnectionTests
{
    private static readonly Guid Container=Guid.Parse("00000000-0000-0000-0000-000000000001");
    private sealed class Headset : IHeadsetProvider
    {
        public bool Connected=true;
        public int Queries;
        public Task<HeadsetReading> QueryAsync(CancellationToken cancellation)
        {
            Interlocked.Increment(ref Queries);
            return Task.FromResult(Connected?new HeadsetReading(HeadsetState.On,DateTimeOffset.UtcNow,69,ContainerId:Container):new(HeadsetState.Unknown,DateTimeOffset.UtcNow));
        }
    }
    private sealed class Audio : IAudioService
    {
        private readonly object gate=new();
        private bool present=true;
        public Action? OnNextRead;
        public int RoleWrites;
        private readonly AsyncLocal<Action?> externalRead=new();
        private readonly AsyncLocal<Action?> externalSnapshotReturned=new();
        private readonly Dictionary<AudioRole,string?> roles=Enum.GetValues<AudioRole>().ToDictionary(r=>r,_=>(string?)"speaker");
        public event Action? Changed;
        public event Action<AudioRole,string?>? DefaultChanged;
        public Task InitializeAsync()=>Task.CompletedTask;
        public Task<AudioSnapshot> ReadAsync(bool peaks=false)
        {
            externalRead.Value?.Invoke();
            Interlocked.Exchange(ref OnNextRead,null)?.Invoke();
            AudioSnapshot snapshot;
            lock(gate)
            {
                List<AudioOutput> outputs=[new("speaker","喇叭",true,.2f,false,null,[])];
                if(present)outputs.Add(new("headset","PRO X 2",true,.5f,false,null,[],Container,true));
                snapshot=new AudioSnapshot(outputs,roles.ToDictionary(),DateTimeOffset.UtcNow);
            }
            externalSnapshotReturned.Value?.Invoke();
            return Task.FromResult(snapshot);
        }
        public Task<IReadOnlyDictionary<AudioRole,string?>> ReadRolesAsync(){lock(gate)return Task.FromResult<IReadOnlyDictionary<AudioRole,string?>>(roles.ToDictionary());}
        public Task<bool> IsAvailableAsync(string id){lock(gate)return Task.FromResult(id=="speaker"||id=="headset"&&present);}
        public Task SetRoleAsync(AudioRole role,string id,Func<bool> current)
        {
            if(!current())throw new OperationCanceledException();
            Interlocked.Increment(ref RoleWrites);
            lock(gate)roles[role]=id;
            DefaultChanged?.Invoke(role,id);return Task.CompletedTask;
        }
        public void Device(bool connected,bool notifyDefaults,bool restoreDefaults=true)
        {
            lock(gate){present=connected;if(!connected||restoreDefaults)foreach(var role in Enum.GetValues<AudioRole>())roles[role]=connected?"headset":"speaker";}
            if(notifyDefaults&&(!connected||restoreDefaults))foreach(var role in Enum.GetValues<AudioRole>())DefaultChanged?.Invoke(role,connected?"headset":"speaker");
            Changed?.Invoke();
        }
        public void External(string id)
        {
            lock(gate)foreach(var role in Enum.GetValues<AudioRole>())roles[role]=id;
            foreach(var role in Enum.GetValues<AudioRole>())DefaultChanged?.Invoke(role,id);
            Changed?.Invoke();
        }
        public void ExternalWithBlockedRead(string id,Action block)
        {externalRead.Value=block;try{External(id);}finally{externalRead.Value=null;}}
        public void DeviceWithBlockedRead(bool connected,Action block)
        {externalRead.Value=block;try{Device(connected,true);}finally{externalRead.Value=null;}}
        public void NotifyDefaultsAfterSnapshot(string id,Action after)
        {
            Action? once=after;
            externalSnapshotReturned.Value=()=>Interlocked.Exchange(ref once,null)?.Invoke();
            try{foreach(var role in Enum.GetValues<AudioRole>())DefaultChanged?.Invoke(role,id);}
            finally{externalSnapshotReturned.Value=null;}
        }
        public Task SetEndpointVolumeAsync(string id,float volume)=>Task.CompletedTask;
        public Task SetEndpointMuteAsync(string id,bool muted)=>Task.CompletedTask;
        public Task SetSessionVolumeAsync(string id,IEnumerable<string> ids,float volume)=>Task.CompletedTask;
        public Task SetSessionMuteAsync(string id,IEnumerable<string> ids,bool muted)=>Task.CompletedTask;
        public Task PlayTestToneAsync(string id)=>Task.CompletedTask;
        public Task StopTestToneAsync()=>Task.CompletedTask;
        public void Dispose() { }
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while(!condition())await Task.Delay(20,timeout.Token);
    }
    [Fact] public async Task ExclusionSavePreservesActiveAutomaticPolicyAndFailedSavePreservesEffectiveSettings()
    {
        string directory=Path.Combine(Path.GetTempPath(),"aom-exclusions-controller-"+Guid.NewGuid().ToString("N"));
        var audio=new Audio();var headset=new Headset();
        new SettingsStore(directory).Save(new(){Automatic=true,Priority=["headset","speaker"],Aliases=new(){{"speaker","桌面"}}});
        try
        {
            using var controller=new AppController(directory,audio,headset);
            await controller.StartAsync();await Until(()=>controller.Snapshot.CurrentId=="headset"&&controller.Snapshot.RolesConsistent);
            int writes=audio.RoleWrites;
            await controller.SaveAsync(s=>s with{QuickMenuExclusions=new(){{AppExclusions.SystemSounds,"系統音效"}},LastPage="exclusions"});
            int queries=headset.Queries;await Until(()=>headset.Queries>=queries+3);
            Assert.True(controller.Settings.Automatic);Assert.Equal(writes,audio.RoleWrites);Assert.Equal("headset",controller.Snapshot.CurrentId);
            Assert.Equal(new[]{"headset","speaker"},controller.Settings.Priority);Assert.Equal("桌面",controller.Settings.Aliases["speaker"]);
            using(var locked=new FileStream(Path.Combine(directory,"settings.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                await Assert.ThrowsAnyAsync<IOException>(()=>controller.SaveAsync(s=>s with{QuickMenuExclusions=[]}));
            Assert.Single(controller.Settings.QuickMenuExclusions);Assert.Single(new SettingsStore(directory).Load().Settings.QuickMenuExclusions);
            Assert.True(controller.Settings.Automatic);Assert.Equal(writes,audio.RoleWrites);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Theory]
    [InlineData(true,true)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public async Task ReplugKeepsAutomaticWithEarlyNotificationReadbackOrNoSystemRestoration(bool notifyDefaults,bool restoreDefaults)
    {
        string directory=Path.Combine(Path.GetTempPath(),"aom-reconnect-"+Guid.NewGuid().ToString("N"));
        var audio=new Audio();var headset=new Headset();
        new SettingsStore(directory).Save(new(){Automatic=true,Priority=["headset","speaker"]});
        try
        {
            using var controller=new AppController(directory,audio,headset);
            await controller.StartAsync();
            await Until(()=>controller.Snapshot.CurrentId=="headset"&&controller.Snapshot.RolesConsistent);
            headset.Connected=false;audio.Device(false,true);
            await Until(()=>controller.Headset.State==HeadsetState.Unknown&&controller.Snapshot.CurrentId=="speaker");
            Assert.True(controller.Settings.Automatic);
            int queries=headset.Queries;
            headset.Connected=true;audio.Device(true,notifyDefaults,restoreDefaults);
            await Until(()=>headset.Queries>=queries+3&&controller.Headset.State==HeadsetState.On);
            Assert.True(controller.Settings.Automatic);
            Assert.True(controller.Snapshot.RolesConsistent);Assert.Equal("headset",controller.Snapshot.CurrentId);
            // Reproduce a notification arriving while the monitor owns the controller gate.
            // Its asynchronous classification must finish before another automatic write.
            audio.OnNextRead=()=>audio.External("speaker");
            await Until(()=>!controller.Settings.Automatic);
            Assert.Equal("speaker",controller.Snapshot.CurrentId);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact] public async Task DelayedDisconnectNotificationAfterReplugDoesNotStopAutomatic()
    {
        string directory=Path.Combine(Path.GetTempPath(),"aom-reconnect-delayed-"+Guid.NewGuid().ToString("N"));
        var audio=new Audio();var headset=new Headset();
        new SettingsStore(directory).Save(new(){Automatic=true,Priority=["headset","speaker"]});
        using var release=new ManualResetEventSlim();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller=new AppController(directory,audio,headset);
        var diagnostic=new System.Collections.Concurrent.ConcurrentQueue<string>();
        controller.Diagnostic+=diagnostic.Enqueue;
        try
        {
            await controller.StartAsync();
            await Until(()=>controller.Snapshot.CurrentId=="headset"&&controller.Snapshot.RolesConsistent);
            audio.DeviceWithBlockedRead(false,()=>{entered.TrySetResult();release.Wait();});
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            int queries=headset.Queries;
            audio.Device(true,false);
            release.Set();
            await Until(()=>headset.Queries>=queries+3&&controller.Headset.State==HeadsetState.On);
            Assert.True(controller.Settings.Automatic,string.Join(Environment.NewLine,diagnostic));
            Assert.True(controller.Snapshot.RolesConsistent);
            Assert.Equal("headset",controller.Snapshot.CurrentId);
        }
        finally{release.Set();controller.Dispose();Directory.Delete(directory,true);}
    }
    [Fact] public async Task ReplugBetweenDefaultSnapshotAndAvailabilityCheckKeepsAutomatic()
    {
        string directory=Path.Combine(Path.GetTempPath(),"aom-reconnect-read-race-"+Guid.NewGuid().ToString("N"));
        var audio=new Audio();var headset=new Headset();
        new SettingsStore(directory).Save(new(){Automatic=true,Priority=["headset","speaker"]});
        try
        {
            using var controller=new AppController(directory,audio,headset);
            await controller.StartAsync();
            await Until(()=>controller.Snapshot.CurrentId=="headset"&&controller.Snapshot.RolesConsistent);
            int queries=headset.Queries;
            audio.Device(false,false);
            audio.NotifyDefaultsAfterSnapshot("speaker",()=>audio.Device(true,false));
            await Until(()=>headset.Queries>=queries+3&&controller.Headset.State==HeadsetState.On);
            Assert.True(controller.Settings.Automatic);
            Assert.Equal("headset",controller.Snapshot.CurrentId);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact] public async Task AnotherExternalChoiceDuringReturnStillStopsAutomatic()
    {
        string directory=Path.Combine(Path.GetTempPath(),"aom-reconnect-choice-"+Guid.NewGuid().ToString("N"));
        var audio=new Audio();var headset=new Headset();
        new SettingsStore(directory).Save(new(){Automatic=true,Priority=["headset","speaker"]});
        try
        {
            using var controller=new AppController(directory,audio,headset);
            await controller.StartAsync();
            await Until(()=>controller.Snapshot.CurrentId=="headset"&&controller.Snapshot.RolesConsistent);
            headset.Connected=false;audio.Device(false,true);
            await Until(()=>controller.Headset.State==HeadsetState.Unknown&&controller.Snapshot.CurrentId=="speaker");
            audio.Device(true,true); // Device returned; HID remains unknown, so the return has not settled.
            await Until(()=>controller.Snapshot.CurrentId=="headset");
            audio.External("speaker");
            await Until(()=>!controller.Settings.Automatic);
            Assert.Equal("speaker",controller.Snapshot.CurrentId);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact] public async Task DisposeWaitsForInFlightExternalNotificationBeforeReleasingSettings()
    {
        string directory=Path.Combine(Path.GetTempPath(),"aom-exit-"+Guid.NewGuid().ToString("N"));
        var audio=new Audio();var headset=new Headset{Connected=false};
        new SettingsStore(directory).Save(new(){Automatic=true,Priority=["speaker"]});
        var controller=new AppController(directory,audio,headset);
        using var release=new ManualResetEventSlim();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? notification=null,disposing=null;
        try
        {
            await controller.StartAsync();await Until(()=>headset.Queries>0);
            notification=Task.Run(()=>audio.ExternalWithBlockedRead("headset",()=>{entered.TrySetResult();release.Wait();}));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            disposing=Task.Run(controller.Dispose);
            Assert.NotSame(disposing,await Task.WhenAny(disposing,Task.Delay(100)));
            release.Set();await notification;await disposing;
            Directory.Delete(directory,true);Assert.False(Directory.Exists(directory));
        }
        finally
        {
            release.Set();if(notification is not null)await notification;if(disposing is not null)await disposing;else controller.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
