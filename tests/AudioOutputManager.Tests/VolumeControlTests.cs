using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class VolumeControlTests
{
    [Fact]
    public async Task AudioReadbackDoesNotWaitForTheUiDispatcherToResume()
    {
        var ui=new HeldUiContext();
        var write=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool readbackStarted=false;
        Task operation;
        var previous=SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(ui);
            var control=new VolumeControl(new(.3f,false),_=>write.Task,_=>Task.CompletedTask,()=>
            {
                readbackStarted=true;return Task.FromResult(new VolumeValue(.8f,false));
            });
            control.Changed+=()=>{};
            operation=control.SetVolumeAsync(.8f);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        write.SetResult();
        await ui.Posted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        bool readBeforeUiResume=readbackStarted;
        ui.Drain();
        await operation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(readBeforeUiResume,"音訊寫入已完成，但讀回仍被 UI 排程擋住。");
    }

    private sealed class HeldUiContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> work=new();
        public TaskCompletionSource Posted {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback,object? state)
        {work.Enqueue(()=>callback(state));Posted.TrySetResult();}
        public void Drain(){while(work.TryDequeue(out var action))action();}
    }

    [Fact]
    public async Task LatestIntentFinishesEvenWhileUiNotificationsAreQueued()
    {
        var ui=new HeldUiContext();
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        float actual=.3f;var writes=new List<float>();
        var previous=SynchronizationContext.Current;VolumeControl control;Task first;
        try
        {
            SynchronizationContext.SetSynchronizationContext(ui);
            control=new(new VolumeValue(.3f,true),async value=>{await release.Task.ConfigureAwait(false);lock(writes)writes.Add(value);actual=value;},_=>throw new Exception("Volume must not unmute"),()=>Task.FromResult(new VolumeValue(actual,true)));
            control.Changed+=()=>{};
            first=control.SetVolumeAsync(.4f);
            _=control.SetVolumeAsync(.6f);
            _=control.SetVolumeAsync(.8f);
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
        release.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(.8f,actual);Assert.Equal(new VolumeValue(.8f,true),control.Value);
        lock(writes)Assert.Equal(new[]{.4f,.8f},writes);
        Assert.True(ui.Posted.Task.IsCompleted);
        ui.Drain();
    }

    [Fact]
    public async Task QueuedOlderSnapshotsCannotUndoConfirmedAudioValues()
    {
        var start=DateTimeOffset.UtcNow;
        var confirmedAt=start.AddSeconds(2);
        var control=new VolumeControl(new VolumeReading(new(.3f,false),start),_=>Task.CompletedTask,_=>Task.CompletedTask,
            ()=>Task.FromResult(new VolumeReading(new(.8f,true),confirmedAt)));
        await control.SetVolumeAsync(.8f);
        Assert.False(control.Observe(new(.3f,false),start.AddSeconds(1)));
        Assert.Equal(new VolumeValue(.8f,true),control.Value);
        Assert.True(control.Observe(new(.6f,false),confirmedAt.AddSeconds(1)));
        Assert.Equal(new VolumeValue(.6f,false),control.Value);
    }

    [Fact]
    public async Task RapidMuteUsesLatestIntentAndSharesImmediateFeedback()
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool actual=false;int writes=0,views=0;VolumeValue firstView=default,secondView=default;
        var control=new VolumeControl(new(.3f,false),_=>Task.CompletedTask,async m=>{writes++;await release.Task;actual=m;},()=>Task.FromResult(new VolumeValue(.3f,actual)));
        control.Changed+=()=>views++;
        control.Changed+=()=>firstView=control.Value;control.Changed+=()=>secondView=control.Value;
        Task first=control.ToggleMuteAsync();Assert.True(control.Value.Muted);Assert.True(firstView.Muted);Assert.Equal(firstView,secondView);
        Task last=control.ToggleMuteAsync();Assert.False(control.Value.Muted);Assert.False(secondView.Muted);Assert.Equal(firstView,secondView);
        control.Observe(new(.3f,true));Assert.False(control.Value.Muted);
        release.SetResult();await Task.WhenAll(first,last);
        Assert.False(actual);Assert.False(control.Value.Muted);Assert.Equal(2,writes);Assert.True(views>=2);
    }

    [Fact]
    public async Task DragCoalescesAndAlwaysConfirmsFinalValueWithoutUnmuting()
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        float actual=.3f;var writes=new List<float>();
        var control=new VolumeControl(new(.3f,true),async v=>{writes.Add(v);await release.Task;actual=v;},_=>throw new Exception("must not unmute"),()=>Task.FromResult(new VolumeValue(actual,true)));
        var first=control.SetVolumeAsync(.31f);
        for(int i=32;i<=80;i++)_=control.SetVolumeAsync(i/100f);
        control.Observe(new(.1f,false));Assert.Equal(.8f,control.Value.Volume);
        release.SetResult();await first;
        Assert.Equal(.8f,actual);Assert.Equal(.8f,control.Value.Volume);Assert.True(control.Value.Muted);Assert.Equal(2,writes.Count);
    }

    [Fact]
    public async Task FailureRollsBackAndAcceptsNextExternalValue()
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var control=new VolumeControl(new(.3f,false),async _=>{await release.Task;throw new InvalidOperationException("device removed");},_=>Task.CompletedTask,()=>Task.FromResult(new VolumeValue(.3f,false)));
        var task=control.SetVolumeAsync(.9f);Assert.Equal(.9f,control.Value.Volume);
        release.SetResult();await Assert.ThrowsAsync<InvalidOperationException>(()=>task);
        Assert.Equal(.3f,control.Value.Volume);
        control.Observe(new(.4f,true));Assert.Equal(new(.4f,true),control.Value);
    }
}
