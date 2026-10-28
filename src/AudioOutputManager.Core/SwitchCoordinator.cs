namespace AudioOutputManager.Core;

public interface IRoleController
{
    Task<IReadOnlyDictionary<AudioRole, string?>> ReadRolesAsync();
    Task<bool> IsAvailableAsync(string id);
    Task SetRoleAsync(AudioRole role, string id, Func<bool> stillCurrent);
}
public sealed record SwitchResult(bool Success, bool Interrupted, bool Restored, IReadOnlyDictionary<AudioRole,string?> Actual, string Message)
{
    public bool TargetUnavailable {get;init;}
}

public sealed class SwitchCoordinator(IRoleController audio, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1,1);
    private readonly object stateLock = new();
    private readonly Dictionary<AudioRole,string> expected = [];
    private long generation;
    public long Generation=>Interlocked.Read(ref generation);

    /// <summary>Call before accepting an external choice or changed settings; in-flight old work stops.</summary>
    public void Invalidate() { Interlocked.Increment(ref generation); }
    public bool AcceptNotification(AudioRole role, string? id)
    {
        lock (stateLock)
        {
            if (expected.TryGetValue(role,out string? target) && target == id) { expected.Remove(role); return true; }
        }
        return false;
    }
    private async Task Write(AudioRole role,string id,Func<bool> stillCurrent)
    {
        lock(stateLock) expected[role] = id;
        try { await audio.SetRoleAsync(role,id,stillCurrent); }
        catch { lock(stateLock) expected.Remove(role); throw; }
    }

    public async Task<SwitchResult> SwitchAsync(string target, CancellationToken cancellation = default,long? decisionGeneration=null)
    {
        long requestedGeneration = decisionGeneration??Generation;
        await gate.WaitAsync(cancellation);
        try
        {
            var before = await audio.ReadRolesAsync();
            bool Current() => requestedGeneration == Interlocked.Read(ref generation) && !cancellation.IsCancellationRequested;
            async Task<SwitchResult> Interrupted() => new(false,true,false,await audio.ReadRolesAsync(),"操作已被新的狀態取代。");
            if (!Current()) return await Interrupted();
            string error = "切換失敗";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!Current()) return await Interrupted();
                if (!await audio.IsAvailableAsync(target)) return new(false,false,false,await audio.ReadRolesAsync(),"目標裝置已不可用。") { TargetUnavailable=true };
                // A native write may notify coupled roles before their individual calls run.
                // Register the complete intent first so that notification cannot cancel its own operation.
                lock(stateLock)
                    foreach(var role in Enum.GetValues<AudioRole>()) expected[role]=target;
                foreach (var role in Enum.GetValues<AudioRole>())
                {
                    if (!Current()) return await Interrupted();
                    try { await Write(role,target,Current); }
                    catch(OperationCanceledException) when(!Current()) { return await Interrupted(); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; }
                }
                long started = clock.GetTimestamp();
                do
                {
                    if (!Current()) return await Interrupted();
                    var actual = await audio.ReadRolesAsync();
                    if (actual.Count == 3 && actual.Values.All(x => x == target)) return new(true,false,false,actual,"三種預設輸出已讀回確認。");
                    await Task.Delay(TimeSpan.FromMilliseconds(50),clock,cancellation);
                } while (clock.GetElapsedTime(started) < TimeSpan.FromSeconds(1));
                if (attempt == 0) await Task.Delay(TimeSpan.FromMilliseconds(500),clock,cancellation);
            }
            if (!Current()) return await Interrupted();
            var partial = await audio.ReadRolesAsync();
            // Restore only roles this operation actually moved, and never overwrite a new choice.
            lock(stateLock)
                foreach(var role in Enum.GetValues<AudioRole>())
                    if(before.GetValueOrDefault(role) is {} previous)expected[role]=previous;
            foreach(var role in Enum.GetValues<AudioRole>())
            {
                if (!Current()) return await Interrupted();
                string? previous = before.GetValueOrDefault(role);
                if (partial.GetValueOrDefault(role) == target && previous is not null && previous != target && await audio.IsAvailableAsync(previous))
                {
                    try { await Write(role,previous,Current); }
                    catch(OperationCanceledException) when(!Current()) { return await Interrupted(); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { error = ex.Message; }
                }
            }
            var final = await audio.ReadRolesAsync();
            bool restored = before.All(x => final.GetValueOrDefault(x.Key) == x.Value);
            return new(false,false,restored,final,restored ? "切換失敗，已還原可還原的輸出。" : "切換失敗，輸出未一致：" + error);
        }
        finally { gate.Release(); }
    }
}
