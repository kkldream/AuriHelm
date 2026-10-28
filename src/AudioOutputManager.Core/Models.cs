namespace AudioOutputManager.Core;

public enum HeadsetState { Unknown, On, Off }
public enum AudioRole { Console, Multimedia, Communications }
public sealed record HeadsetReading(HeadsetState State, DateTimeOffset ObservedAt, int? Battery = null, string? Error = null, Guid? ContainerId = null);
public sealed record AudioSession(string Id, string Name, string AppIdentity, Guid Grouping, float Volume, bool Muted, float? Peak, string? IconFile = null, string? IconPath = null, long IconRevision = 0);
public sealed record AudioOutput(string Id, string Name, bool Available, float Volume, bool Muted, float? Peak, IReadOnlyList<AudioSession> Sessions, Guid? ContainerId = null, bool IsProX2 = false);
public sealed record AudioSnapshot(IReadOnlyList<AudioOutput> Outputs, IReadOnlyDictionary<AudioRole, string?> Defaults, DateTimeOffset ObservedAt, string? Error = null)
{
    public string? CurrentId => Defaults.GetValueOrDefault(AudioRole.Multimedia);
    public bool RolesConsistent => Defaults.Count == 3 && Defaults.Values.All(x => x is not null && x == CurrentId);
}

public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public bool Automatic { get; init; }
    public string[] Priority { get; init; } = [];
    public Dictionary<string, string> Aliases { get; init; } = [];
    public Dictionary<string, string> KnownDevices { get; init; } = [];
    public int OffConfirmationSeconds { get; init; } = 2;
    public string Language { get; init; } = "system";
    public string Theme { get; init; } = "system";
    public Dictionary<string, string> Hotkeys { get; init; } = [];
    public Dictionary<string, string> QuickMenuExclusions { get; init; } = [];
    public bool StartAtLogin { get; init; }
    public string LastPage { get; init; } = "devices";
    public int WindowWidth { get; init; } = 960;
    public int WindowHeight { get; init; } = 720;
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public bool Maximized { get; init; }

    public void Validate()
    {
        if (Version != CurrentVersion) throw new InvalidDataException("不支援此設定版本；不覆寫未知版本。");
        if (OffConfirmationSeconds is < 1 or > 5) throw new InvalidDataException("耳機關閉確認時間必須為 1–5 秒。");
        if (Priority is null || Aliases is null || KnownDevices is null || Hotkeys is null || Priority.Any(string.IsNullOrWhiteSpace) || Priority.Distinct().Count() != Priority.Length)
            throw new InvalidDataException("裝置優先序或設定內容不正確。");
        if (Theme is not ("system" or "light" or "dark") || Language is not ("system" or "zh-Hant" or "en"))
            throw new InvalidDataException("語言或外觀設定不正確。");
        if (QuickMenuExclusions is null || QuickMenuExclusions.Any(p => AppExclusions.Key(p.Key) != p.Key || string.IsNullOrWhiteSpace(p.Value)))
            throw new InvalidDataException("應用程式排除設定不正確。");
    }
}
