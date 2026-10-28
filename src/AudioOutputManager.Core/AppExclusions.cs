namespace AudioOutputManager.Core;

/// <summary>Persistent app identity and quick-menu visibility; never changes mixer grouping or audio.</summary>
public static class AppExclusions
{
    public const string SystemSounds = "system:sounds";

    public static string? Key(string identity)
    {
        if (string.Equals(identity, SystemSounds, StringComparison.OrdinalIgnoreCase)) return SystemSounds;
        if (identity.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase))
        {
            string value = identity[6..].Trim();
            return value.Length > 0 ? "aumid:" + value.ToUpperInvariant() : null;
        }
        if (!identity.StartsWith("exe:", StringComparison.OrdinalIgnoreCase)) return null;
        string path = identity[4..].Replace('/', '\\');
        if (!Path.IsPathFullyQualified(path)) return null;
        try { return "exe:" + Path.GetFullPath(path).ToUpperInvariant(); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static IReadOnlyList<AudioSession> Visible(IEnumerable<AudioSession> sessions, IReadOnlyDictionary<string,string> exclusions)
    {
        var keys = exclusions.Keys.Select(Key).Where(k => k is not null).ToHashSet(StringComparer.Ordinal);
        return sessions.Where(s => Key(s.AppIdentity) is not { } key || !keys.Contains(key)).ToArray();
    }
}

public sealed record ExclusionCandidate(string Id, string? Key, string Name, bool Detected, int Groups, AudioSession? Session, bool Saved = false, IReadOnlyList<AudioSession>? Sessions = null);

/// <summary>A page draft independent of session refreshes. A failed save never advances the baseline.</summary>
public sealed class AppExclusionDraft
{
    private Dictionary<string,string> baseline;
    private readonly Dictionary<string,string> selected;
    public string Search { get; set; } = "";
    public bool OnlyExcluded { get; set; }
    public int Count => selected.Count;
    public bool IsDirty => !baseline.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(selected.Keys);

    public AppExclusionDraft(IReadOnlyDictionary<string,string> exclusions)
    {
        baseline = exclusions.ToDictionary(p => AppExclusions.Key(p.Key) ?? throw new InvalidDataException("排除身分不正確。"), p => p.Value, StringComparer.Ordinal);
        selected = new(baseline, StringComparer.Ordinal);
    }

    public bool IsExcluded(string? key) => key is not null && selected.ContainsKey(key);
    public void Set(ExclusionCandidate app, bool exclude)
    {
        if (app.Key is null) return;
        if (exclude) selected[app.Key] = app.Name; else selected.Remove(app.Key);
    }

    public IReadOnlyList<ExclusionCandidate> Candidates(AudioSnapshot snapshot)
    {
        var apps = snapshot.Outputs.SelectMany(d => d.Sessions.Select(s => (Output:d.Id, Session:s)))
            .GroupBy(x => AppExclusions.Key(x.Session.AppIdentity) ?? x.Session.AppIdentity, StringComparer.Ordinal)
            .Select(g => new ExclusionCandidate(g.Key, AppExclusions.Key(g.First().Session.AppIdentity), g.First().Session.Name, true,
                g.Select(x => (x.Output, x.Session.Grouping)).Distinct().Count(),
                g.Select(x=>x.Session).OrderByDescending(s=>!string.IsNullOrWhiteSpace(s.IconPath)).ThenByDescending(s=>!string.IsNullOrWhiteSpace(s.IconFile)).First(),
                Sessions:g.Select(x=>x.Session).ToArray())).ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var (key,name) in baseline.Concat(selected))
            apps.TryAdd(key, new(key,key,name,false,0,null));
        return apps.Values.Select(a => a with {Saved=a.Key is not null && baseline.ContainsKey(a.Key)})
            .OrderBy(a => a.Key != AppExclusions.SystemSounds).ThenBy(a => a.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id,StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<ExclusionCandidate> Filter(IEnumerable<ExclusionCandidate> apps) => apps
        .Where(a => (!OnlyExcluded || IsExcluded(a.Key)) && a.Name.Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();

    public async Task ApplyAsync(Func<Dictionary<string,string>,Task> save)
    {
        var next = new Dictionary<string,string>(selected, StringComparer.Ordinal);
        await save(next);
        baseline = next;
    }
}
