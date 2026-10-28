namespace AudioOutputManager.Core;

public enum AppIconSourceKind { Resource, Package, Executable, Thumbnail }
public sealed record AppIconSource(AppIconSourceKind Kind,string Value);
public sealed record AppIconRequest(string Identity,string Revision,int Size,IReadOnlyList<AppIconSource> Sources,long SourceRevision=0)
{
    public bool SystemSounds=>Identity==AppExclusions.SystemSounds;
    public string Key=>Identity+"|"+Size+"|"+string.Join("|",Sources.Select(s=>$"{s.Kind}:{s.Value.ToUpperInvariant()}"));
    public static AppIconRequest Create(IEnumerable<AudioSession> sessions,int size)
    {
        var members=sessions.OrderBy(s=>s.Id,StringComparer.Ordinal).ToArray();
        if(members.Length==0)throw new ArgumentException("需要音訊工作階段。",nameof(sessions));
        var sources=new List<AppIconSource>();
        void Add(AppIconSourceKind kind,string? value)
        {
            if(!string.IsNullOrWhiteSpace(value)&&!sources.Any(s=>s.Kind==kind&&string.Equals(s.Value,value,StringComparison.OrdinalIgnoreCase)))sources.Add(new(kind,value));
        }
        foreach(var s in members)Add(AppIconSourceKind.Resource,s.IconPath);
        foreach(var s in members)if(s.AppIdentity.StartsWith("aumid:",StringComparison.OrdinalIgnoreCase))Add(AppIconSourceKind.Package,s.AppIdentity[6..]);
        foreach(var s in members)
        {
            Add(AppIconSourceKind.Executable,s.IconFile);
            if(s.AppIdentity.StartsWith("exe:",StringComparison.OrdinalIgnoreCase))Add(AppIconSourceKind.Executable,s.AppIdentity[4..]);
        }
        foreach(var source in sources.Where(s=>s.Kind is AppIconSourceKind.Resource or AppIconSourceKind.Executable).ToArray())Add(AppIconSourceKind.Thumbnail,source.Value);
        return new(AppExclusions.Key(members[0].AppIdentity)??members[0].AppIdentity,string.Join("|",members.Select(s=>$"{s.Id}:{s.IconRevision}")),Math.Clamp(size,16,256),sources,members.Max(s=>s.IconRevision));
    }
}
