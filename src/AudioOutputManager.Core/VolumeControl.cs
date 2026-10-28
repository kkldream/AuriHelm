namespace AudioOutputManager.Core;

public readonly record struct VolumeValue(float Volume,bool Muted);
public readonly record struct VolumeReading(VolumeValue Value,DateTimeOffset ObservedAt);

/// <summary>UI intents remain immediate; audio writes and confirmation never wait for the UI dispatcher.</summary>
public sealed class VolumeControl
{
    private readonly object gate=new();
    private readonly Func<float,Task> writeVolume;
    private readonly Func<bool,Task> writeMute;
    private readonly Func<Task<VolumeReading>> readback;
    private readonly SynchronizationContext? ui;
    private readonly Action<Action>? dispatch;
    private VolumeReading confirmed;
    private float? desiredVolume;
    private bool? desiredMute;
    private long volumeVersion,muteVersion;
    private Task? sending;
    private long lastVolumeWrite;
    public VolumeControl(VolumeValue initial,Func<float,Task> writeVolume,Func<bool,Task> writeMute,Func<Task<VolumeValue>> readback)
        :this(new VolumeReading(initial,DateTimeOffset.MinValue),writeVolume,writeMute,async()=>new VolumeReading(await readback().ConfigureAwait(false),DateTimeOffset.UtcNow)) { }
    public VolumeControl(VolumeReading initial,Func<float,Task> writeVolume,Func<bool,Task> writeMute,Func<Task<VolumeReading>> readback,Action<Action>? dispatch=null)
    {
        confirmed=initial;this.writeVolume=writeVolume;this.writeMute=writeMute;this.readback=readback;
        ui=SynchronizationContext.Current;this.dispatch=dispatch;
    }
    public VolumeValue Value {get {lock(gate)return new(desiredVolume??confirmed.Value.Volume,desiredMute??confirmed.Value.Muted);}}
    public event Action? Changed;
    public bool Observe(VolumeValue actual,DateTimeOffset? observedAt=null)
    {
        var reading=new VolumeReading(actual,observedAt??DateTimeOffset.UtcNow);
        lock(gate)
        {
            if(reading.ObservedAt<confirmed.ObservedAt)return false;
            confirmed=reading;
        }
        Changed?.Invoke();return true;
    }
    public Task SetVolumeAsync(float volume)
    {
        lock(gate){desiredVolume=Math.Clamp(volume,0,1);volumeVersion++;}
        Changed?.Invoke();return Start();
    }
    public Task ToggleMuteAsync()
    {
        lock(gate){desiredMute=!(desiredMute??confirmed.Value.Muted);muteVersion++;}
        Changed?.Invoke();return Start();
    }
    private Task Start()
    {
        TaskCompletionSource completion;
        lock(gate)
        {
            if(sending is not null)return sending;
            completion=new(TaskCreationOptions.RunContinuationsAsynchronously);sending=completion.Task;
        }
        _=SendAsync(completion);return completion.Task;
    }
    private void PublishConfirmation()
    {
        if(dispatch is not null)dispatch(()=>Changed?.Invoke());
        else if(ui is not null&&!ReferenceEquals(SynchronizationContext.Current,ui))ui.Post(_=>Changed?.Invoke(),null);
        else Changed?.Invoke();
    }
    private async Task SendAsync(TaskCompletionSource completion)
    {
        Exception? failure=null;
        while(true)
        {
            double remaining;
            lock(gate)
            {
                if(!desiredVolume.HasValue&&!desiredMute.HasValue)
                {
                    // Finish atomically with checking pending intents, so a new caller cannot join a finished sender.
                    sending=null;
                    if(failure is null)completion.TrySetResult();else completion.TrySetException(failure);
                    return;
                }
                remaining=desiredVolume.HasValue&&!desiredMute.HasValue&&lastVolumeWrite!=0
                    ?33-System.Diagnostics.Stopwatch.GetElapsedTime(lastVolumeWrite).TotalMilliseconds:0;
            }
            if(remaining>0)await Task.Delay(TimeSpan.FromMilliseconds(remaining)).ConfigureAwait(false);
            long v,m;float? volume;bool? muted;
            lock(gate){v=volumeVersion;m=muteVersion;volume=desiredVolume;muted=desiredMute;}
            VolumeReading? reading=null;
            try
            {
                if(muted.HasValue)await writeMute(muted.Value).ConfigureAwait(false);
                if(volume.HasValue)
                {
                    lock(gate)lastVolumeWrite=System.Diagnostics.Stopwatch.GetTimestamp();
                    await writeVolume(volume.Value).ConfigureAwait(false);
                }
                reading=await readback().ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                failure=ex;
                try{reading=await readback().ConfigureAwait(false);}catch{/* Removed devices retain the last confirmed value. */}
            }
            lock(gate)
            {
                if(reading is {} actual&&actual.ObservedAt>=confirmed.ObservedAt)confirmed=actual;
                if(v==volumeVersion)desiredVolume=null;
                if(m==muteVersion)desiredMute=null;
            }
            try{PublishConfirmation();}catch(Exception ex){failure??=ex;}
        }
    }
}
