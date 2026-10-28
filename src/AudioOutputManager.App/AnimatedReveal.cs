using System.Diagnostics;
using AudioOutputManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AudioOutputManager.App;

/// <summary>One reusable disclosure for the picker and every full-panel section. Content stays mounted.</summary>
internal sealed class AnimatedReveal : Grid
{
    private readonly FrameworkElement content;
    private readonly RevealTransition transition;
    private bool animating;
    private readonly TranslateTransform translation=new();
    private readonly RotateTransform? rotation;
    private readonly Func<bool> animations;
    private readonly Action? changed;
    private double naturalHeight;
    private static int nextTraceId;
    private readonly int traceId=Interlocked.Increment(ref nextTraceId);
    private long traceStarted;
    public double RevealHeight {get;private set;}
    public AnimatedReveal(FrameworkElement content,bool open,FontIcon? arrow,Func<bool> animations,Action? changed=null)
    {
        this.content=content;this.animations=animations;this.changed=changed;transition=new(open);
        VerticalAlignment=VerticalAlignment.Top;
        content.VerticalAlignment=VerticalAlignment.Top;content.RenderTransform=translation;Children.Add(content);
        if(arrow is not null){rotation=new(){Angle=open?180:0};arrow.RenderTransform=rotation;arrow.RenderTransformOrigin=new(.5,.5);}
        SizeChanged+=(_,e)=>{Clip=new RectangleGeometry{Rect=new(0,0,e.NewSize.Width,e.NewSize.Height)};};
        Loaded+=(_,_)=>{if(!animating)SetOpen(transition.Open,false);};
        Unloaded+=(_,_)=>StopAnimation();
        Visibility=open?Visibility.Visible:Visibility.Collapsed;
    }
    private static double Now=>Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency;
    public void SetOpen(bool open,bool animate=true)
    {
        traceStarted=AcceptanceTrace.Now;
        bool motion=animate&&animations()&&XamlRoot is not null;
        transition.Set(open,Now,motion);
        Visibility=Visibility.Visible;IsHitTestVisible=open;
        double width=ActualWidth>0?ActualWidth:((Parent as FrameworkElement)?.ActualWidth??400);
        content.Measure(new Size(Math.Max(1,width),double.PositiveInfinity));naturalHeight=content.DesiredSize.Height;
        if(motion&&!animating){animating=true;CompositionTarget.Rendering+=OnRendering;}Draw();
    }
    private void OnRendering(object? sender,object args)=>Draw();
    private void StopAnimation(){if(!animating)return;CompositionTarget.Rendering-=OnRendering;animating=false;}
    private void Draw()
    {
        double now=Now;
        if(!animations()&&transition.Running(now))transition.Set(transition.Open,now,false);
        double progress=transition.Progress(now);bool running=transition.Running(now);
        Height=RevealHeight=Math.Max(0,naturalHeight*progress);content.Opacity=progress;translation.Y=4*(1-progress);
        if(rotation is not null)rotation.Angle=180*progress;
        AcceptanceTrace.Record("reveal-frame",traceStarted,new{Id=traceId,Open=transition.Open,Progress=progress,Height=RevealHeight,Running=running});
        changed?.Invoke();
        if(!running)
        {
            StopAnimation();Visibility=transition.Open?Visibility.Visible:Visibility.Collapsed;
            if(transition.Open){Height=double.NaN;content.Opacity=1;translation.Y=0;}
        }
    }
}
