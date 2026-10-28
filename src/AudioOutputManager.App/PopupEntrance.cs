using System.Diagnostics;
using AudioOutputManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AudioOutputManager.App;

/// <summary>One short entrance for the whole quick menu, independent of picker disclosure state.</summary>
internal sealed class PopupEntrance
{
    private readonly FrameworkElement target;
    private readonly Func<bool> animations;
    private readonly RevealTransition transition=new(false);
    private readonly TranslateTransform offset=new();
    private bool running;
    private long started;
    private static double Now=>Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency;
    public PopupEntrance(FrameworkElement target,Func<bool> animations)
    {this.target=target;this.animations=animations;target.RenderTransform=offset;}
    public void Start()
    {
        Stop();started=AcceptanceTrace.Now;
        double now=Now;transition.Set(false,now,false);transition.Set(true,now,animations());
        running=true;CompositionTarget.Rendering+=OnRendering;Draw();
    }
    public void Stop()
    {
        if(running){CompositionTarget.Rendering-=OnRendering;running=false;}
        target.Opacity=1;offset.Y=0;
    }
    private void OnRendering(object? sender,object args)=>Draw();
    private void Draw()
    {
        double now=Now;if(!animations())transition.Set(true,now,false);
        double progress=transition.Progress(now);target.Opacity=progress;offset.Y=8*(1-progress);
        AcceptanceTrace.Record("quick-open-frame",started,new{Progress=progress,Y=offset.Y,Running=transition.Running(now)});
        if(!transition.Running(now))Stop();
    }
}
