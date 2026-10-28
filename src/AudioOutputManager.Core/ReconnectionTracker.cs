namespace AudioOutputManager.Core;

/// <summary>
/// Tracks observed removal/return episodes, not the origin of an IMM default notification.
/// Only a listed endpoint observed missing and then available qualifies. The five-second cap bounds
/// incomplete episodes; stable readback plus fresh HID confirmation closes them earlier.
/// </summary>
public sealed class ReconnectionTracker(TimeProvider? time=null)
{
    private readonly TimeProvider clock=time??TimeProvider.System;
    private HashSet<string>? present;
    private readonly HashSet<string> missing=[];
    private readonly Dictionary<string,long> returned=[];
    private static readonly TimeSpan NotificationLimit=TimeSpan.FromSeconds(5);

    // The controller serializes all calls with its operation gate.
    public bool Observe(AudioSnapshot snapshot)
    {
        var active=snapshot.Outputs.Where(d=>d.Available).Select(d=>d.Id).ToHashSet(StringComparer.Ordinal);
        if(present is null){present=active;return false;}
        bool changed=!present.SetEquals(active);
        foreach(string id in present.Except(active)){missing.Add(id);returned.Remove(id);}
        foreach(string id in active)
            if(missing.Remove(id))returned[id]=clock.GetTimestamp();
        present=active;
        return changed;
    }
    public bool IsExpectedReturn(string? id,IReadOnlyList<string> priority)
        =>id is not null&&priority.Contains(id)&&returned.TryGetValue(id,out var arrival)&&clock.GetElapsedTime(arrival)<=NotificationLimit;

    // A removal callback can wait behind the operation gate until its old endpoint is back.
    // Compare receipt time with the observed return so later user choices stay effective.
    public bool IsDelayedRemoval(string? previous,long receivedAt,IReadOnlyList<string> priority)
        =>IsExpectedReturn(previous,priority)&&receivedAt<returned[previous!];

    public void Settle(AudioSnapshot snapshot,string? headsetId,HeadsetState confirmed,bool readingFresh)
    {
        foreach(var (id,arrival) in returned.ToArray())
        {
            bool expired=clock.GetElapsedTime(arrival)>NotificationLimit;
            var device=snapshot.Outputs.FirstOrDefault(d=>d.Id==id);
            bool verified=device is not null&&(!device.IsProX2||(id==headsetId&&confirmed==HeadsetState.On&&readingFresh));
            if(expired||(snapshot.RolesConsistent&&snapshot.CurrentId==id&&verified))returned.Remove(id);
        }
    }
    public void ClearPending()=>returned.Clear();
}
