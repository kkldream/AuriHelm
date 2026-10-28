using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Automation;
using AudioOutputManager.Core;

namespace AudioOutputManager.App;

internal sealed class VolumeRow : Grid
{
    private readonly FocusedWheelSlider slider;
    private readonly Button mute;
    private readonly FontIcon muteIcon;
    private readonly TextBlock number;
    private readonly VolumeControl control;
    private readonly Action<Exception> error;
    private readonly bool quick;
    private bool refreshing,muted,dragging;
    private readonly PeakMeterState meter=new();

    public VolumeRow(Theme theme,string label,VolumeControl control,Action<Exception> error,bool quick=false)
    {
        this.control=control;this.error=error;this.quick=quick;
        ColumnSpacing=12;MinHeight=30;
        ColumnDefinitions.Add(new(){Width=new(30)});ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=new(quick?42:32)});
        muteIcon=new(){Glyph="\uE767",FontSize=16};
        mute=new(){Content=muteIcon,Padding=new(4),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),Foreground=theme.Text,BorderThickness=new(0)};
        AutomationProperties.SetName(mute,label+" 靜音 / Mute");
        mute.Click+=async (_,_)=>
        {
            long started=AcceptanceTrace.Now;
            try
            {
                var write=control.ToggleMuteAsync();
                AcceptanceTrace.Confirm(write,started,"mute",quick?"quick":"full");
                AcceptanceTrace.Record("input-ui",started,new{View=quick?"quick":"full",Action="mute"});AcceptanceTrace.Frame("input-frame",started,quick?"quick":"full");
                await write;AcceptanceTrace.Record("input-ui-settled",started,new{Action="mute",View=quick?"quick":"full"});
            }
            catch(Exception ex){error(ex);}
        };
        mute.FocusVisualPrimaryBrush=theme.Blue;mute.FocusVisualSecondaryBrush=theme.Head;
        slider=new FocusedWheelSlider(theme.Peak){Minimum=0,Maximum=100,StepFrequency=1,SmallChange=1,LargeChange=5,IsThumbToolTipEnabled=false,Foreground=theme.Blue,Background=theme.Line,HorizontalAlignment=HorizontalAlignment.Stretch,Height=30,MinHeight=0,Template=TrackTemplate(quick)};
        slider.FocusVisualPrimaryBrush=theme.Blue;slider.FocusVisualSecondaryBrush=theme.Head;slider.UseSystemFocusVisuals=true;
        number=new(){FontSize=quick?18:14,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Foreground=theme.Text};Grid.SetColumn(number,2);
        AutomationProperties.SetName(slider,label);ToolTipService.SetToolTip(slider,label);
        slider.AddHandler(PointerPressedEvent,new PointerEventHandler((_,_)=>dragging=true),true);
        async void FinishDrag(object sender,PointerRoutedEventArgs args)
        {if(!dragging)return;dragging=false;try{await control.SetVolumeAsync((float)slider.Value/100);}catch(Exception ex){error(ex);}}
        slider.AddHandler(PointerReleasedEvent,new PointerEventHandler(FinishDrag),true);
        slider.AddHandler(PointerCaptureLostEvent,new PointerEventHandler(FinishDrag),true);
        slider.ValueChanged+=async (_,_)=>
        {
            if(refreshing)return;
            long started=AcceptanceTrace.Now;
            number.Text=((int)slider.Value).ToString();
            try
            {
                var write=control.SetVolumeAsync((float)slider.Value/100);
                AcceptanceTrace.Confirm(write,started,"volume",quick?"quick":"full");
                AcceptanceTrace.Record("input-ui",started,new{View=quick?"quick":"full",Action="volume"});AcceptanceTrace.Frame("input-frame",started,quick?"quick":"full");
                await write;AcceptanceTrace.Record("input-ui-settled",started,new{Action="volume",View=quick?"quick":"full"});
            }
            catch(Exception ex) {error(ex);}
        };
        var track=new Grid();track.Children.Add(slider);
        Grid.SetColumn(track,1);
        Children.Add(mute);Children.Add(track);Children.Add(number);
        Loaded+=(_,_)=>{control.Changed+=Refresh;Refresh();};
        Unloaded+=(_,_)=>control.Changed-=Refresh;
    }
    public void Update(float volume,bool muted,float? activity,DateTimeOffset observedAt)
    {
        // Meter chronology is independent of newer volume confirmations that did not sample peaks.
        meter.Observe(activity,observedAt);
        control.Observe(new(volume,muted),observedAt);
        Refresh();
        AcceptanceTrace.Record("peak-state",AcceptanceTrace.Now,new{View=quick?"quick":"full",Measured=activity.HasValue,Peak=meter.Value,Muted=this.muted});
    }
    private void Refresh()
    {
        refreshing=true;
        try
        {
            var value=control.Value;muted=value.Muted;
            if(!dragging) {slider.Value=Math.Clamp(value.Volume*100,0,100);number.Text=((int)Math.Round(value.Volume*100)).ToString();}
            muteIcon.Glyph=muted?"\uE74F":"\uE767";slider.Opacity=muted?.4:1;DrawPeak();
        }
        finally {refreshing=false;}
    }
    public void Describe(string description)=>AutomationProperties.SetHelpText(slider,description);
    private void DrawPeak()
    {
        slider.SetActivity(muted?0:meter.Value);
    }
    private static ControlTemplate TrackTemplate(bool quick)=>(ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load($$"""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Grid x:Name="SliderContainer" Background="Transparent" Margin="4,0">
            <Grid x:Name="HorizontalTemplate" Height="30">
              <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <Rectangle x:Name="HorizontalTrackRect" Grid.ColumnSpan="3" Height="4" RadiusX="2" RadiusY="2" Fill="{TemplateBinding Background}"/>
              <Rectangle x:Name="HorizontalDecreaseRect" Height="4" RadiusX="2" RadiusY="2" Fill="{TemplateBinding Foreground}"/>
              <Rectangle x:Name="ActivityMeter" Grid.ColumnSpan="3" Height="3" HorizontalAlignment="Left" VerticalAlignment="Center" IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw"/>
              <Thumb x:Name="HorizontalThumb" Grid.Column="1" Canvas.ZIndex="1" Width="{{(quick?8:6)}}" Height="{{(quick?22:17)}}" Background="{TemplateBinding Foreground}" AutomationProperties.AccessibilityView="Raw">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="4"/></ControlTemplate></Thumb.Template>
              </Thumb>
            </Grid>
            <Grid x:Name="VerticalTemplate" Visibility="Collapsed"/>
          </Grid>
        </ControlTemplate>
        """);
    private sealed class FocusedWheelSlider : Slider
    {
        private readonly Brush activityBrush;
        private Rectangle? activity;
        private float? level;
        public FocusedWheelSlider(Brush activityBrush)
        {this.activityBrush=activityBrush;SizeChanged+=(_,_)=>DrawActivity();}
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            activity=GetTemplateChild("ActivityMeter") as Rectangle;
            if(activity is not null)activity.Fill=activityBrush;
            DrawActivity();
        }
        public void SetActivity(float? value){level=value;DrawActivity();}
        private void DrawActivity()
        {
            if(activity is null)return;
            activity.Visibility=level.HasValue?Visibility.Visible:Visibility.Collapsed;
            activity.Width=Math.Max(0,ActualWidth-8)*Math.Clamp(level??0,0,1);
        }
        protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
        {if(FocusState!=FocusState.Unfocused)base.OnPointerWheelChanged(e);}
    }
}
