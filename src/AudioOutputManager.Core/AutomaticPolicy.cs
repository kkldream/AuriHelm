namespace AudioOutputManager.Core;

/// <summary>Only fresh observations advance confirmation. This class never operates Windows.</summary>
public sealed class AutomaticPolicy
{
    private DateTimeOffset? offSince;
    private DateTimeOffset? lastObservation;
    private int offSamples, onSamples;
    public HeadsetState Confirmed { get; private set; } = HeadsetState.Unknown;

    public void Reset()
    {
        offSince = lastObservation = null;
        offSamples = onSamples = 0;
        Confirmed = HeadsetState.Unknown;
    }

    public void Observe(HeadsetReading reading, DateTimeOffset now, int offSeconds)
    {
        if (reading.ObservedAt > now || now - reading.ObservedAt > TimeSpan.FromSeconds(2)) { Reset(); return; }
        if (lastObservation is { } last && reading.ObservedAt <= last) return;
        if (lastObservation is { } prior && reading.ObservedAt - prior > TimeSpan.FromSeconds(2)) Reset();
        lastObservation = reading.ObservedAt;
        switch (reading.State)
        {
            case HeadsetState.Unknown: Reset(); break;
            case HeadsetState.On:
                offSince = null; offSamples = 0;
                if (++onSamples >= 2) Confirmed = HeadsetState.On;
                break;
            case HeadsetState.Off:
                onSamples = 0;
                offSince ??= reading.ObservedAt;
                if (++offSamples >= 3 && reading.ObservedAt - offSince >= TimeSpan.FromSeconds(offSeconds)) Confirmed = HeadsetState.Off;
                break;
        }
    }

    public string? Choose(AppSettings settings, AudioSnapshot snapshot, string? headsetId, bool readingFresh)
    {
        if (!settings.Automatic) return null;
        bool currentAvailable = snapshot.Outputs.Any(d => d.Id == snapshot.CurrentId && d.Available)
            && !(snapshot.CurrentId == headsetId && Confirmed == HeadsetState.Off && readingFresh);
        foreach (string id in settings.Priority)
        {
            if (!snapshot.Outputs.Any(d => d.Id == id && d.Available)) continue;
            if (id == headsetId)
            {
                if (!readingFresh || Confirmed == HeadsetState.Unknown)
                {
                    if (currentAvailable) return null;
                    continue;
                }
                if (Confirmed == HeadsetState.Off) continue;
            }
            return id == snapshot.CurrentId && snapshot.RolesConsistent ? null : id;
        }
        return null;
    }

    public static string? Next(IReadOnlyList<string> priority, IEnumerable<string> available, string? current)
    {
        var usable = available.ToHashSet(StringComparer.Ordinal);
        int start = -1;
        for (int i = 0; i < priority.Count; i++) if (priority[i] == current) start = i;
        for (int step = 1; step <= priority.Count; step++)
        {
            string candidate = priority[(start + step) % priority.Count];
            if (usable.Contains(candidate)) return candidate == current ? null : candidate;
        }
        return null;
    }
}
