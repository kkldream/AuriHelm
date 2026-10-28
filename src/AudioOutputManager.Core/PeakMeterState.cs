namespace AudioOutputManager.Core;

/// <summary>Stores activity samples independently from volume and mute confirmations.</summary>
public sealed class PeakMeterState
{
    public float? Value {get;private set;}
    private DateTimeOffset observedAt=DateTimeOffset.MinValue;
    public void Observe(float? peak,DateTimeOffset timestamp)
    {
        // A value-only snapshot has no meter sample. It is not a measurement of silence.
        if(!peak.HasValue||timestamp<observedAt)return;
        Value=Math.Clamp(peak.Value,0,1);observedAt=timestamp;
    }
}
