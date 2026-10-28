using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;

namespace AudioOutputManager.App;

/// <summary>Explicit local acceptance instrumentation. Without the marker, no file or render handler is created.</summary>
internal static class AcceptanceTrace
{
    private static readonly string DirectoryPath=AppContext.BaseDirectory;
    public static readonly bool Enabled=File.Exists(Path.Combine(DirectoryPath,"acceptance-trace.enabled"));
    private static readonly object gate=new();
    private static StreamWriter? output;
    private static bool unavailable;
    public static long Now=>Stopwatch.GetTimestamp();
    public static void Record(string kind,long started,object? data=null)
    {
        if(!Enabled)return;
        lock(gate)
        {
            if(unavailable)return;
            try
            {
                output??=new StreamWriter(Path.Combine(DirectoryPath,"acceptance-trace.jsonl"),true){AutoFlush=true};
                output.WriteLine(JsonSerializer.Serialize(new{Kind=kind,Process=Environment.ProcessId,Timestamp=Now,Milliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds,Data=data}));
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){unavailable=true;}
        }
    }
    public static void Frame(string kind,long started,string? view=null)
    {
        if(!Enabled)return;
        EventHandler<object>? rendered=null;
        rendered=(_,_)=>{CompositionTarget.Rendering-=rendered;Record(kind,started,new{View=view});};
        CompositionTarget.Rendering+=rendered;
    }
    public static void Confirm(Task operation,long started,string action,string view)
    {
        if(!Enabled)return;
        _=operation.ContinueWith(t=>Record(t.IsCompletedSuccessfully?"input-confirmed":"input-failed",started,new{Action=action,View=view}),
            CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
    }
}
