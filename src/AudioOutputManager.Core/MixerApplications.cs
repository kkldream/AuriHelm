namespace AudioOutputManager.Core;

/// <summary>The application rows shared by the quick menu and the full mixer.</summary>
public static class MixerApplications
{
    public static IReadOnlyList<AudioSession[]> Group(IEnumerable<AudioSession> sessions) => sessions
        .GroupBy(s => AppExclusions.Key(s.AppIdentity) is { } key
            ? (Application: true, Id: key)
            : (Application: false, Id: s.Id))
        .Select(g => g.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray()).ToArray();

    public static VolumeValue Value(IEnumerable<AudioSession> sessions)
    {
        var members = sessions.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (members.Length == 0) throw new InvalidOperationException("音訊工作階段已結束。");
        return new(members[0].Volume, members.All(s => s.Muted));
    }
}
