using AudioOutputManager.Core;

namespace AudioOutputManager.Windows;

/// <summary>The Windows audio boundary; controllers can replay device events without operating hardware.</summary>
public interface IAudioService : IRoleController, IDisposable
{
    event Action? Changed;
    event Action<AudioRole,string?>? DefaultChanged;
    Task InitializeAsync();
    Task<AudioSnapshot> ReadAsync(bool peaks=false);
    Task SetEndpointVolumeAsync(string id,float volume);
    Task SetEndpointMuteAsync(string id,bool muted);
    Task SetSessionVolumeAsync(string deviceId,IEnumerable<string> ids,float volume);
    Task SetSessionMuteAsync(string deviceId,IEnumerable<string> ids,bool muted);
    Task PlayTestToneAsync(string id);
    Task StopTestToneAsync();
}

public interface IHeadsetProvider
{
    Task<HeadsetReading> QueryAsync(CancellationToken cancellation);
}

/// <summary>Fast value notifications and visibility-scoped metering; neither enumerates topology.</summary>
public interface ILiveAudioService
{
    event Action<AudioSnapshot>? ValuesChanged;
    Task<AudioSnapshot> ReadPeaksAsync(IReadOnlyList<PeakTarget> visible);
    Task<AudioSnapshot> ReadValuesAsync(string deviceId,IReadOnlySet<string>? sessions=null);
}
public sealed record PeakTarget(string DeviceId,IReadOnlySet<string> SessionIds,bool Endpoint);
