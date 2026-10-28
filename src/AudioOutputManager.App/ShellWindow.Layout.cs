using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AudioOutputManager.App;

internal sealed partial class ShellWindow
{
    private UIElement BrandMark()
    {
        var canvas=new Canvas{Width=64,Height=64};
        var mark=(Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"M47.6 16.4A22 22 0 1 0 54 32M43 21L52 12M22 29V36M30 24V40M38 28V36\" StrokeThickness=\"4.6\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" StrokeLineJoin=\"Round\"/>");
        mark.Stroke=theme.Blue;canvas.Children.Add(mark);
        return new Viewbox{Child=canvas,Width=22,Height=22};
    }
    private void StyleDialog(ContentDialog dialog)
    {
        dialog.RequestedTheme=fullRoot.RequestedTheme;dialog.Background=theme.Card;dialog.Foreground=theme.Text;dialog.BorderBrush=theme.Line;
        dialog.Resources["ContentDialogBackground"]=theme.Head;dialog.Resources["ContentDialogTopOverlay"]=theme.Card;dialog.Resources["ContentDialogBorderBrush"]=theme.Line;dialog.Resources["ContentDialogSeparatorBorderBrush"]=theme.Line;
        dialog.Resources["ButtonBackground"]=theme.Card;dialog.Resources["ButtonBackgroundPointerOver"]=theme.Hover;dialog.Resources["ButtonBackgroundPressed"]=theme.Hover;dialog.Resources["ButtonForeground"]=theme.Text;
        dialog.Resources["TextControlBackground"]=theme.Panel;dialog.Resources["TextControlForeground"]=theme.Text;
    }
    private FontIcon Icon(string glyph,int size=18)=>new(){Glyph=glyph,FontSize=size,Foreground=theme.Muted,VerticalAlignment=VerticalAlignment.Center};
    private Button NavigationButton(string label,Func<Task> action)
    {
        var button=Button(label,action);
        // Each button owns its state brushes. The default Button color transitions must not animate a shared theme brush.
        button.Template=(ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load($$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Grid>
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal"/>
                    <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="NavigationSurface.Background" Value="{{theme.Hover.Color}}"/></VisualState.Setters></VisualState>
                    <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="NavigationSurface.Background" Value="{{theme.Card.Color}}"/></VisualState.Setters></VisualState>
                    <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="NavigationSurface.Opacity" Value="0.5"/></VisualState.Setters></VisualState>
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <Border x:Name="NavigationSurface" Background="{TemplateBinding Background}" CornerRadius="{TemplateBinding CornerRadius}" Padding="{TemplateBinding Padding}">
                  <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
              </Grid>
            </ControlTemplate>
            """);
        button.UseSystemFocusVisuals=true;return button;
    }
    private StackPanel Actions(params UIElement[] children)
    {
        var panel=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
        foreach(var child in children)panel.Children.Add(child);return panel;
    }
    private Border Inset(UIElement child,bool highlight=false)=>new(){Child=child,Padding=new(16,14,16,14),Background=highlight?theme.Hover:null};
    private UIElement Rows(params UIElement[] rows)
    {
        var panel=new StackPanel();
        for(int i=0;i<rows.Length;i++)panel.Children.Add(new Border{Child=rows[i],BorderBrush=theme.Line,BorderThickness=new(0,i==0?0:1,0,0)});
        return panel;
    }
    // The same row wraps at small widths, rather than clipping its actions.
    private Grid Row(UIElement label,UIElement? action=null)
    {
        var row=new Grid{Padding=new(16,14,16,14),ColumnSpacing=14,RowSpacing=10};
        row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.RowDefinitions.Add(new(){Height=GridLength.Auto});row.RowDefinitions.Add(new(){Height=GridLength.Auto});
        row.Children.Add(label);
        if(action is not null)
        {
            if(action is FrameworkElement aligned)aligned.VerticalAlignment=VerticalAlignment.Center;
            row.Children.Add(action);Grid.SetColumn((FrameworkElement)action,1);
            row.SizeChanged+=(_,_)=>{bool wrap=row.ActualWidth<510;Grid.SetRow((FrameworkElement)action,wrap?1:0);Grid.SetColumn((FrameworkElement)action,wrap?0:1);Grid.SetColumnSpan((FrameworkElement)label,wrap?2:1);};
        }
        return row;
    }
    private Border Tag(string label)=>new(){Child=Text(label,12),Background=theme.Hover,Padding=new(8,3,8,3),CornerRadius=new(4)};
    private Button FlatButton(string label,Func<Task> action)
    {
        var button=Button(label,action);button.Background=new SolidColorBrush(Colors.Transparent);button.BorderThickness=new(0);return button;
    }
    private UIElement Disclosure(string id,string label,UIElement content,bool initial=false)
    {
        bool open=disclosures.GetValueOrDefault(id,initial);
        var arrow=Icon("\uE70D",12);
        var body=new AnimatedReveal(new Border{Child=content},open,arrow,()=>systemColors.AnimationsEnabled);
        var button=FlatButton(label,()=>
        {
            open=!open;disclosures[id]=open;body.SetOpen(open);
            return Task.CompletedTask;
        });
        button.Content=Actions(arrow,Text(label,12,true));button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new(16,12,16,12);
        AutomationProperties.SetName(button,label);var section=Stack(button,body);section.Spacing=0;return section;
    }
    private string DeviceGlyph(AudioOutputManager.Core.AudioOutput output)=>output.Id==controller.HeadsetId||output.IsProX2?"\uE7F6":output.Name.Contains("Display",StringComparison.OrdinalIgnoreCase)||output.Name.Contains("HDMI",StringComparison.OrdinalIgnoreCase)?"\uE7F4":"\uE7F5";
    private string DeviceState(AudioOutputManager.Core.AudioOutput output)
        =>!output.Available?T("離線","Offline"):(output.Id==controller.Snapshot.CurrentId?T("目前輸出 · ","Current output · "):"")+(output.Id==controller.HeadsetId?HeadsetText():T("可用","Available"))+(controller.Settings.Priority.Contains(output.Id)?"":T(" · 未加入優先序"," · Not in priority"));
    private string BatteryText()=>controller.Headset.Battery is {} b
        ?DateTimeOffset.UtcNow-controller.Headset.ObservedAt>TimeSpan.FromSeconds(2)?T($"電量過期（{b}%）",$"Stale battery ({b}%)"):$"{b}%"
        :T("電量未知","Battery unknown");
}
