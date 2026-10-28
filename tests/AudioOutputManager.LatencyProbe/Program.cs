using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AudioOutputManager.Core;
using AudioOutputManager.Windows;

// Manual hardware acceptance only. Not part of dotnet test or the normal build.
if(args is not ["--allow-audio-changes",var match,var destination])
{
    Console.Error.WriteLine("僅於取得當次音訊驗收授權後執行：--allow-audio-changes <裝置名稱片段> <全新輸出目錄>。會改音量／靜音並於 finally 還原，不切換預設輸出。這不量測 UI 或實際發聲延遲。");return 2;
}
if(Directory.Exists(destination))throw new IOException("驗收目錄已存在，拒絕覆寫舊基準。");
Directory.CreateDirectory(destination);
using var cancel=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
using var audio=new CoreAudioService();using var observer=new CoreAudioService();
await audio.InitializeAsync();await observer.InitializeAsync();
var initial=await audio.ReadAsync();
var device=initial.Outputs.Single(d=>d.Available&&d.Name.Contains(match,StringComparison.OrdinalIgnoreCase));
var session=device.Sessions.First(s=>s.Name=="系統音效");
var ids=new HashSet<string>{session.Id};
await File.WriteAllTextAsync(Path.Combine(destination,"baseline.json"),JsonSerializer.Serialize(initial));
using var log=new StreamWriter(Path.Combine(destination,"samples.jsonl")){AutoFlush=true};
var samples=new ConcurrentBag<Sample>();
var meterTimes=new ConcurrentBag<double>();
var targets=new[]{new PeakTarget(device.Id,ids,true)};
var before=audio.Counters;var cpu=Process.GetCurrentProcess().TotalProcessorTime;var elapsed=Stopwatch.StartNew();
using var metersStop=new CancellationTokenSource();
var meters=Task.Run(async()=>
{
    while(!metersStop.IsCancellationRequested)
    {
        var frame=Stopwatch.StartNew();await audio.ReadPeaksAsync(targets);meterTimes.Add(frame.Elapsed.TotalMilliseconds);
        try{await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1,50-frame.Elapsed.TotalMilliseconds)),metersStop.Token);}catch(OperationCanceledException){}
    }
});
int exit=0;
try
{
    foreach(string path in new[]{"endpoint-volume","endpoint-mute","session-volume","session-mute"})
    {
        for(int index=0;index<30;index++)
        {
            cancel.Token.ThrowIfCancellationRequested();
            if(File.Exists(Path.Combine(destination,"cancel")))throw new OperationCanceledException();
            bool endpoint=path.StartsWith("endpoint"),mute=path.EndsWith("mute");
            float originalVolume=endpoint?device.Volume:session.Volume;
            bool originalMute=endpoint?device.Muted:session.Muted;
            float desired=index%2==0?(originalVolume>=.01f?originalVolume-.01f:originalVolume+.01f):originalVolume;
            bool desiredMute=index%2==0?!originalMute:originalMute;
            bool Match(AudioSnapshot snapshot)
            {
                var d=snapshot.Outputs.FirstOrDefault(d=>d.Id==device.Id);if(d is null)return false;
                if(endpoint)return mute?d.Muted==desiredMute:Math.Abs(d.Volume-desired)<.0001f;
                var s=d.Sessions.FirstOrDefault(s=>s.Id==session.Id);return s is not null&&(mute?s.Muted==desiredMute:Math.Abs(s.Volume-desired)<.0001f);
            }
            var watch=Stopwatch.StartNew();var notification=new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(AudioSnapshot snapshot){if(Match(snapshot))notification.TrySetResult(watch.Elapsed.TotalMilliseconds);}
            observer.ValuesChanged+=Changed;
            try
            {
                if(endpoint){if(mute)await audio.SetEndpointMuteAsync(device.Id,desiredMute);else await audio.SetEndpointVolumeAsync(device.Id,desired);}
                else{if(mute)await audio.SetSessionMuteAsync(device.Id,ids,desiredMute);else await audio.SetSessionVolumeAsync(device.Id,ids,desired);}
                double writeAndOwnReadbackMs=watch.Elapsed.TotalMilliseconds;
                var actual=await observer.ReadValuesAsync(device.Id,endpoint?null:ids);
                double confirmedMs=watch.Elapsed.TotalMilliseconds;
                double? notified=null;
                try{notified=await notification.Task.WaitAsync(TimeSpan.FromSeconds(2),cancel.Token);}catch(TimeoutException){}
                var sample=new Sample(path,index,writeAndOwnReadbackMs,confirmedMs,notified,Match(actual));samples.Add(sample);
                await log.WriteLineAsync(JsonSerializer.Serialize(sample));
            }
            finally{observer.ValuesChanged-=Changed;}
            await Task.Delay(60,cancel.Token);
        }
    }
}
catch(OperationCanceledException){exit=3;Console.Error.WriteLine("驗收已取消，還原測試目標。");}
finally
{
    metersStop.Cancel();await meters;
    // Restore only the endpoint and session we touched. Do not rewrite roles or other applications.
    await audio.SetEndpointVolumeAsync(device.Id,device.Volume);await audio.SetEndpointMuteAsync(device.Id,device.Muted);
    await audio.SetSessionVolumeAsync(device.Id,ids,session.Volume);await audio.SetSessionMuteAsync(device.Id,ids,session.Muted);
    var restored=await observer.ReadValuesAsync(device.Id);var restoredSession=await observer.ReadValuesAsync(device.Id,ids);
    var d=restored.Outputs.Single(d=>d.Id==device.Id);var s=restoredSession.Outputs.Single(d=>d.Id==device.Id).Sessions.Single(s=>s.Id==session.Id);
    if(Math.Abs(d.Volume-device.Volume)>.0001f||d.Muted!=device.Muted||Math.Abs(s.Volume-session.Volume)>.0001f||s.Muted!=session.Muted)throw new Exception("還原讀回失敗，請保留 baseline.json。");
}
var summary=samples.GroupBy(s=>s.Path).Select(g=>new{Path=g.Key,Count=g.Count(),P95=Percentile(g.Select(s=>s.ConfirmedMs)),Max=g.Max(s=>s.ConfirmedMs),NotificationP95=Percentile(g.Where(s=>s.NotificationMs.HasValue).Select(s=>s.NotificationMs!.Value)),Failures=g.Count(s=>!s.Match||!s.NotificationMs.HasValue)}).ToArray();
var after=audio.Counters;
var result=new{Completed=exit==0,Summary=summary,PeakFrames=meterTimes.Count,PeakP95=Percentile(meterTimes),TopologyRefreshes=after.TopologyReads-before.TopologyReads,Seconds=elapsed.Elapsed.TotalSeconds,CpuMs=(Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds,Restored=true};
await File.WriteAllTextAsync(Path.Combine(destination,"summary.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result));
if(exit==0&&(summary.Length!=4||summary.Any(g=>g.Count!=30||g.P95>150||g.NotificationP95>150||g.Failures>0)))return 1;
return exit;

static double? Percentile(IEnumerable<double> values){var sorted=values.Order().ToArray();return sorted.Length==0?null:sorted[(int)Math.Ceiling(sorted.Length*.95)-1];}
internal sealed record Sample(string Path,int Index,double WriteAndOwnReadbackMs,double ConfirmedMs,double? NotificationMs,bool Match);
