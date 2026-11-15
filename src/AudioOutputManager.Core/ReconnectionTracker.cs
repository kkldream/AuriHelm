namespace AudioOutputManager.Core;

/// <summary>
/// Tracks observed removal/return episodes, not the origin of an IMM default notification.
/// A listed endpoint can return after removal or a reliably observed headset power cycle.
/// The five-second cap bounds incomplete episodes; stable readback plus fresh HID confirmation closes them earlier.
/// </summary>
public sealed class ReconnectionTracker(TimeProvider? time=null)
{
    private readonly TimeProvider clock=time??TimeProvider.System;
    private HashSet<string>? present;
    private readonly HashSet<string> missing=[];
    private readonly Dictionary<string,long> returned=[];
    private readonly Dictionary<string,long> poweredOn=[];
    private readonly Dictionary<string,HeadsetReading> powerReadings=[];
    private static readonly TimeSpan NotificationLimit=TimeSpan.FromSeconds(5);

    // The controller serializes all calls with its operation gate.
    public bool Observe(AudioSnapshot snapshot)
    {
        var active=snapshot.Outputs.Where(d=>d.Available).Select(d=>d.Id).ToHashSet(StringComparer.Ordinal);
        if(present is null){present=active;return false;}
        bool changed=!present.SetEquals(active);
        foreach(string id in present.Except(active)){missing.Add(id);returned.Remove(id);poweredOn.Remove(id);powerReadings.Remove(id);}
        foreach(string id in active)
            if(missing.Remove(id))returned[id]=clock.GetTimestamp();
        present=active;
        return changed;
    }
    public void ObserveHeadset(string? id,HeadsetReading reading)
    {
        if(id is null||reading.State==HeadsetState.Unknown)return;
        var now=clock.GetUtcNow();
        if(reading.ObservedAt>now||now-reading.ObservedAt>TimeSpan.FromSeconds(2))return;
        powerReadings.TryGetValue(id,out var previous);
        if(previous is not null&&reading.ObservedAt<=previous.ObservedAt)return;
        // Unknown never supplies an Off observation. A real Off -> On may precede the
        // driver's default restoration while its USB audio endpoint stays available.
        if(reading.State==HeadsetState.On&&previous?.State==HeadsetState.Off)poweredOn[id]=clock.GetTimestamp();
        if(reading.State==HeadsetState.Off)poweredOn.Remove(id);
        powerReadings[id]=reading;
    }
    private bool IsRecent(Dictionary<string,long> episodes,string id)
        =>episodes.TryGetValue(id,out var arrival)&&clock.GetElapsedTime(arrival)<=NotificationLimit;
    public bool IsExpectedReturn(string? id,IReadOnlyList<string> priority)
        =>id is not null&&priority.Contains(id)&&(IsRecent(returned,id)||IsRecent(poweredOn,id));

    // A removal callback can wait behind the operation gate until its old endpoint is back.
    // Compare receipt time with the observed return so later user choices stay effective.
    public bool IsDelayedRemoval(string? previous,long receivedAt,IReadOnlyList<string> priority)
        =>previous is not null&&priority.Contains(previous)&&returned.TryGetValue(previous,out var arrival)
          &&clock.GetElapsedTime(arrival)<=NotificationLimit&&receivedAt<arrival;

    public void Settle(AudioSnapshot snapshot,string? headsetId,HeadsetState confirmed,bool readingFresh)
    {
        foreach(var episodes in new[]{returned,poweredOn})
        foreach(var (id,arrival) in episodes.ToArray())
        {
            bool expired=clock.GetElapsedTime(arrival)>NotificationLimit;
            var device=snapshot.Outputs.FirstOrDefault(d=>d.Id==id);
            bool verified=device is not null&&(!device.IsProX2||(id==headsetId&&confirmed==HeadsetState.On&&readingFresh));
            if(expired||(snapshot.RolesConsistent&&snapshot.CurrentId==id&&verified))episodes.Remove(id);
        }
    }
    public void ClearPending(){returned.Clear();poweredOn.Clear();powerReadings.Clear();}
}
