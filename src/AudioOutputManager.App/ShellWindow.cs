using AudioOutputManager.Core;
using AudioOutputManager.Windows;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace AudioOutputManager.App;

internal sealed partial class ShellWindow
{
    private readonly AppController controller;
    private readonly Action exit;
    private readonly Window full=new(){Title="AuriHelm"};
    private readonly Window quick=new(){Title="AuriHelm · Quick Menu"};
    private const int QuickFooterHeight=46;
    private readonly Grid fullRoot=new(),quickRoot=new();
    private readonly Border quickBackdrop=new();
    private readonly PopupEntrance quickEntrance;
    private TrayMenuHost? trayMenuHost;
    private readonly StackPanel pageHost=new(){Spacing=16};
    private readonly Grid titleBar=new(){Height=48,Padding=new(20,0,130,0)};
    private readonly Dictionary<string,bool> disclosures=[];
    private readonly Grid nav=new(){ColumnSpacing=6,RowSpacing=6};
    private readonly Border applyDock=new(){Padding=new(24,12,24,12),Visibility=Visibility.Collapsed};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12,VerticalAlignment=VerticalAlignment.Center};
    private readonly Dictionary<string,Action<AudioSnapshot>> audioRows=[];
    private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly DispatcherTimer meters=new(){Interval=TimeSpan.FromMilliseconds(50)};
    private readonly Dictionary<string,VolumeControl> volumeControls=[];
    private readonly Dictionary<string,(string Device,string[]? Sessions)> volumeTargets=[];
    private readonly List<(string Key,string Device,HashSet<string> Sessions,bool Endpoint,FrameworkElement Row)> meterRows=[];
    private readonly ILiveAudioService? liveAudio;
    private DateTimeOffset latestValues;
    private Theme theme=new(false);
    private string currentPage="devices";
    private readonly ViewRefreshTracker viewRefresh=new();
    private bool fullVisible,quickVisible,quitting,rendering,meterBusy,expanded,dirty;
    private Func<Task>? applyPending;
    private DateTimeOffset quickHiddenAt;
    private List<string> draftPriority=[];
    private int draftOff=2;
    private Dictionary<string,string> draftHotkeys=[];
    private readonly LocalDiagnostics diagnostics;
    private readonly NativeShell native;
    private readonly Hotkeys hotkeys;
    private string? uiIssue;
    private readonly HashSet<string> pendingRemovals=[];
    private SizeChangedEventHandler? layoutChanged;
    private Grid? previousAside,previousContent;
    private ScrollViewer? pageScroll;
    private Button? quickSelector;
    private TextBlock? titleName;
    private AnimatedReveal? quickPicker;
    private string quickSelectorName="";
    private long quickFrameStyle;
    private readonly global::Windows.UI.ViewManagement.UISettings systemColors=new();
    private global::Windows.Foundation.TypedEventHandler<global::Windows.UI.ViewManagement.UISettings,object>? colorsChanged;

    public ShellWindow(AppController controller,NativeShell native,Action exit,string directory)
    {
        quickEntrance=new(quickRoot,()=>systemColors.AnimationsEnabled);
        this.controller=controller;this.native=native;this.exit=exit;diagnostics=new(Path.Combine(directory,"logs"));
        liveAudio=controller.Audio as ILiveAudioService;
        if(liveAudio is not null)liveAudio.ValuesChanged+=OnLiveValues;
        hotkeys=new(native);native.Hotkey+=hotkeys.Dispatch;
        hotkeys.Invoked+=async action=>{try{switch(action){case "menu":ToggleQuick();break;case "next":await controller.NextAsync();break;case "automatic":await controller.SetAutomaticAsync(!controller.Settings.Automatic);break;}}catch(Exception ex){Error(ex);}};
        try{using var initial=hotkeys.Prepare(controller.Settings.Hotkeys);initial.Commit();}catch(Exception ex){Error(ex);}
        var frame=new Grid();frame.RowDefinitions.Add(new(){Height=new(48)});frame.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        frame.Children.Add(titleBar);Grid.SetRow(fullRoot,1);frame.Children.Add(fullRoot);full.Content=frame;
        quickBackdrop.Child=quickRoot;quick.Content=quickBackdrop;
        string appIcon=Path.Combine(AppContext.BaseDirectory,"Assets","AuriHelm.ico");
        full.AppWindow.SetIcon(appIcon);quick.AppWindow.SetIcon(appIcon);
        full.ExtendsContentIntoTitleBar=true;full.SetTitleBar(titleBar);
        quick.ExtendsContentIntoTitleBar=true;
        currentPage=controller.Settings.LastPage;
        if(controller.Settings.WindowX is {} initialX&&controller.Settings.WindowY is {} initialY)full.AppWindow.Move(new(initialX,initialY));
        double fullScale=NativeShell.GetDpiForWindow(WindowNative.GetWindowHandle(full))/96d;
        var area=DisplayArea.GetFromWindowId(full.AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        int fullWidth=Math.Min((int)(controller.Settings.WindowWidth*fullScale),area.Width),fullHeight=Math.Min((int)(controller.Settings.WindowHeight*fullScale),area.Height);
        full.AppWindow.Resize(new(fullWidth,fullHeight));
        if(controller.Settings.WindowX is {} x&&controller.Settings.WindowY is {} y)full.AppWindow.Move(new(Math.Clamp(x,area.X,Math.Max(area.X,area.X+area.Width-fullWidth)),Math.Clamp(y,area.Y,Math.Max(area.Y,area.Y+area.Height-fullHeight))));
        if(controller.Settings.Maximized&&full.AppWindow.Presenter is OverlappedPresenter fp)fp.Maximize();
        if(quick.AppWindow.Presenter is OverlappedPresenter presenter) {presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;}
        nint hwnd=WindowNative.GetWindowHandle(quick);
        NativeShell.SetWindowLongPtr(hwnd,-20,(nint)(((long)NativeShell.GetWindowLongPtr(hwnd,-20)|0x80)&~0x40000));
        status.SizeChanged+=(_,_)=>UpdateTitleRegions();titleBar.SizeChanged+=(_,_)=>{SizeTitleName();UpdateTitleRegions();};
        full.AppWindow.Closing+=async (_,args)=> { if(quitting)return;args.Cancel=true;try{if(await ResolvePending()){full.AppWindow.Hide();fullVisible=false;UpdateMeterSchedule();await SaveWindow();}}catch(Exception ex){Error(ex);} };
        quick.AppWindow.Closing+=(_,args)=>{if(quitting)return;args.Cancel=true;HideQuick();};
        quick.Activated+=(_,args)=>{if(args.WindowActivationState==WindowActivationState.Deactivated&&quickVisible)HideQuick();else ApplyQuickFrame();};
        quickRoot.KeyDown+=(_,e)=>{if(e.Key==global::Windows.System.VirtualKey.Escape){if(expanded)SetQuickExpanded(false);else HideQuick();e.Handled=true;}};
        fullRoot.ActualThemeChanged+=(_,_)=>RenderAll();
        colorsChanged=(_,_)=>dispatcher.TryEnqueue(()=>{if(!quitting)RenderAll();});systemColors.ColorValuesChanged+=colorsChanged;
        controller.Updated+=OnUpdate;controller.Issue+=OnIssue;
        controller.Diagnostic+=RecordDiagnostic;
        meters.Tick+=async (_,_)=>
        {
            bool fullMixerVisible=fullVisible&&currentPage=="devices"&&full.AppWindow.Presenter is OverlappedPresenter p&&p.State!=OverlappedPresenterState.Minimized;
            if(meterBusy||(!fullMixerVisible&&!quickVisible)||quitting)return;meterBusy=true;
            var visible=VisibleMeters(fullMixerVisible);
            if(visible.Count==0){meterBusy=false;return;}
            long started=AcceptanceTrace.Now;
            try
            {
                if(liveAudio is not null)
                {
                    var sample=await liveAudio.ReadPeaksAsync(visible);
                    UpdateValues(sample);
                    if(AcceptanceTrace.Enabled)
                        AcceptanceTrace.Record("meter-read",started,new{Targets=visible.Sum(t=>t.SessionIds.Count+(t.Endpoint?1:0)),Peak=sample.Outputs.Select(d=>Math.Max(d.Peak??0,d.Sessions.Select(s=>s.Peak??0).DefaultIfEmpty().Max())).DefaultIfEmpty().Max()});
                    AcceptanceTrace.Frame("meter-frame",started);
                }
            }
            catch(Exception ex){Error(ex);}finally{meterBusy=false;}
        };
        RenderAll();
        AcceptanceTrace.Record("window-start",AcceptanceTrace.Now,new{Width=full.AppWindow.Size.Width,ClientWidth=full.AppWindow.ClientSize.Width,Resizable=(full.AppWindow.Presenter as OverlappedPresenter)?.IsResizable,Animations=systemColors.AnimationsEnabled});
    }
    private bool English=>controller.Settings.Language=="en"||(controller.Settings.Language=="system"&&!new[]{"zh-TW","zh-HK","zh-MO","zh-Hant"}.Any(name=>System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith(name,StringComparison.OrdinalIgnoreCase)));
    private string T(string zh,string en)=>English?en:zh;
    private string Name(AudioOutput d)=>controller.Settings.Aliases.GetValueOrDefault(d.Id,d.Name);
    private string Name(string id)=>controller.Snapshot.Outputs.FirstOrDefault(d=>d.Id==id) is {} d?Name(d):controller.Settings.Aliases.GetValueOrDefault(id,controller.Settings.KnownDevices.GetValueOrDefault(id,T("離線裝置","Offline device")));
    private TextBlock Text(string text,int size=14,bool muted=false)=>new(){Text=text,FontSize=size,Foreground=muted?theme.Muted:theme.Text,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    private Button Button(string text,Func<Task> action)
    {
        var button=new Button{Content=text,Foreground=theme.Text,Background=theme.Card,BorderBrush=theme.Line,Padding=new(12,7,12,7),CornerRadius=new(6),FontSize=14};
        AutomationProperties.SetName(button,text);
        button.FocusVisualPrimaryBrush=theme.Blue;button.FocusVisualSecondaryBrush=theme.Head;
        button.Click+=async(_,_)=>{try{uiIssue=null;await action();}catch(Exception ex){Error(ex);}};return button;
    }
    private Border Card(UIElement content,bool head=false)=>new(){Child=content,Background=head?theme.Head:theme.Card,BorderBrush=theme.Line,BorderThickness=new(1),CornerRadius=new(8)};
    private StackPanel Stack(params UIElement[] children){var stack=new StackPanel{Spacing=4};foreach(var child in children)stack.Children.Add(child);return stack;}
    private void OnIssue(string message)=>dispatcher.TryEnqueue(()=> {uiIssue=message;UpdateStatus();try{diagnostics.Record("operation-error");}catch(IOException){} });
    private void UpdateStatus(){status.Text=uiIssue??(dirty?T("有尚未套用的變更","Unsaved changes"):StatusText());ToolTipService.SetToolTip(status,status.Text);}
    private void SizeTitleName(){if(titleName is not null)titleName.MaxWidth=Math.Max(28,titleBar.ActualWidth-150-26-8-80);}
    private void UpdateTitleRegions()
    {
        if(status.XamlRoot is null||status.ActualWidth<=0)return;
        var rect=status.TransformToVisual(null).TransformBounds(new global::Windows.Foundation.Rect(0,0,status.ActualWidth,status.ActualHeight));
        double scale=status.XamlRoot.RasterizationScale;
        InputNonClientPointerSource.GetForWindowId(full.AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough,[new((int)(rect.X*scale),(int)(rect.Y*scale),(int)(rect.Width*scale),(int)(rect.Height*scale))]);
    }
    private void OnLiveValues(AudioSnapshot snapshot)
    {
        long received=AcceptanceTrace.Now;
        dispatcher.TryEnqueue(()=>
        {
            if(quitting||!UpdateValues(snapshot))return;
            AcceptanceTrace.Record("notification-ui",received);
            if(quickVisible||fullVisible)AcceptanceTrace.Frame("notification-frame",received);
        });
    }
    private void RecordDiagnostic(string code){try{diagnostics.Record(code);}catch(IOException){}catch(UnauthorizedAccessException){}}
    private void Error(Exception error)=>OnIssue(error is InvalidDataException or InvalidOperationException?error.Message:T("操作失敗，請查看設定及裝置狀態。錯誤碼：","Operation failed. Check settings and devices. Code: ")+error.HResult.ToString("X8"));
    private void OnUpdate()=>dispatcher.TryEnqueue(()=>
    {
        if(quitting)return;
        var refresh=viewRefresh.Observe(controller.Snapshot,controller.Settings,controller.HeadsetId,currentPage,dirty);
        if(refresh.Quick)PruneVolumeControls();
        if(refresh.Quick)RenderQuick();
        if(refresh.Full){if(currentPage=="exclusions"&&exclusionList is not null)RefreshExclusionRows();else RenderPage(refresh.PreserveDraft);}
        UpdateValues(controller.Snapshot);UpdateStatus();
    });
    private string StatusText()
    {
        if(uiIssue is not null)return uiIssue;
        if(!English)return controller.Status;
        if(controller.Status.Contains("外部"))return "External output change detected. Automatic switching is off.";
        if(controller.Status.Contains("手動"))return "Automatic switching is off after manual selection.";
        if(controller.Status.Contains("失敗")||controller.Status.Contains("未能"))return "Operation failed. Check device status and diagnostics.";
        if(controller.Status.Contains("可靠"))return "Waiting for a verified headset connection.";
        if(!controller.Settings.Automatic)return "Automatic switching is off. "+(controller.Snapshot.RolesConsistent?"":"Output roles differ.");
        return controller.Settings.Priority.Length==0?"Waiting for priority configuration":controller.Headset.State==HeadsetState.Unknown?"Waiting for device status":"Automatic switching enabled";
    }
    private string HeadsetText()=>DateTimeOffset.UtcNow-controller.Headset.ObservedAt>TimeSpan.FromSeconds(2)?T("狀態未知","Unknown"):controller.Headset.State switch{HeadsetState.On=>T("耳機已開啟","Headset on"),HeadsetState.Off=>T("耳機已關閉","Headset off"),_=>T("狀態未知","Unknown")};
    private void RenderAll()
    {
        if(quitting||rendering)return;rendering=true;
        try
        {
            fullRoot.RequestedTheme=quickRoot.RequestedTheme=Theme.Requested(controller.Settings.Theme);
            theme=new(fullRoot.ActualTheme==ElementTheme.Dark);fullRoot.Background=quickRoot.Background=theme.Panel;
            quickBackdrop.Background=theme.Panel;
            ApplyQuickFrame();
            foreach(var root in new[]{fullRoot,quickRoot})
            {
                foreach(string suffix in new[]{"","PointerOver","Pressed"}){root.Resources["ToggleSwitchFillOn"+suffix]=theme.Blue;root.Resources["ToggleSwitchKnobFillOn"+suffix]=new SolidColorBrush(Colors.White);}
                root.Resources["ButtonBackgroundPointerOver"]=theme.Hover;root.Resources["ButtonBackgroundPressed"]=theme.Hover;root.Resources["ButtonForegroundPointerOver"]=theme.Text;
                root.Resources["SystemControlFocusVisualPrimaryBrush"]=theme.Blue;root.Resources["SystemControlFocusVisualSecondaryBrush"]=theme.Head;
                root.Resources["FocusVisualWhiteStrokeThemeBrush"]=theme.Blue;root.Resources["FocusVisualBlackStrokeThemeBrush"]=theme.Head;
            }
            titleBar.Background=theme.Head;titleBar.Children.Clear();
            titleBar.ColumnDefinitions.Clear();titleBar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});titleBar.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});titleBar.ColumnSpacing=8;
            titleName=Text("AuriHelm");titleName.TextWrapping=TextWrapping.NoWrap;titleName.TextTrimming=TextTrimming.CharacterEllipsis;SizeTitleName();
            titleBar.Children.Add(Actions(BrandMark(),titleName));Grid.SetColumn(status,1);status.Margin=new(0);status.Foreground=theme.Muted;titleBar.Children.Add(status);UpdateStatus();
            full.AppWindow.TitleBar.ButtonBackgroundColor=theme.Head.Color;full.AppWindow.TitleBar.ButtonForegroundColor=theme.Text.Color;
            full.AppWindow.TitleBar.ButtonInactiveBackgroundColor=theme.Head.Color;full.AppWindow.TitleBar.ButtonInactiveForegroundColor=theme.Muted.Color;
            audioRows.Clear();meterRows.Clear();BuildFull();RenderQuick();
        }finally{rendering=false;}
    }
    public void ShowFull()
    {
        if(quitting)return;
        trayMenuHost?.Hide();
        HideQuick();fullVisible=true;
        if(full.AppWindow.Presenter is OverlappedPresenter p&&p.State==OverlappedPresenterState.Minimized)p.Restore();
        full.Activate();NativeShell.SetForegroundWindow(WindowNative.GetWindowHandle(full));
        UpdateMeterSchedule();
    }
    public async void ShowTrayMenu()
    {
        if(quitting)return;
        try
        {
            HideQuick();
            trayMenuHost??=new();
            switch(await trayMenuHost.ShowAsync(T("開啟面板","Open panel"),T("結束程式","Exit")))
            {
                case TrayMenuCommand.OpenPanel:ShowFull();break;
                case TrayMenuCommand.Exit:if(dirty)ShowFull();await QuitAsync();break;
            }
        }
        catch(Exception ex){Error(ex);}
    }
    public void ToggleQuick()
    {
        if(quitting)return;
        trayMenuHost?.Hide();
        if(quickVisible){HideQuick();return;}
        if(DateTimeOffset.UtcNow-quickHiddenAt<TimeSpan.FromMilliseconds(180))return;
        RenderQuick();NativeShell.GetCursorPos(out var point);
        var monitor=new NativeShell.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeShell.MonitorInfo>()};
        if(!NativeShell.GetMonitorInfo(NativeShell.MonitorFromPoint(point,2),ref monitor))return;
        quick.AppWindow.Move(new(point.X,point.Y));
        double scale=NativeShell.GetDpiForWindow(WindowNative.GetWindowHandle(quick))/96d;
        int width=Math.Min((int)(420*scale),monitor.Work.Right-monitor.Work.Left),height=Math.Min((int)(QuickHeight()*scale),monitor.Work.Bottom-monitor.Work.Top-16);
        quick.AppWindow.Resize(new(width,height));
        quick.AppWindow.Move(new(Math.Clamp(point.X-width/2,monitor.Work.Left,monitor.Work.Right-width),monitor.Work.Bottom-height-8));
        quickVisible=true;quickEntrance.Start();quick.Activate();NativeShell.SetForegroundWindow(WindowNative.GetWindowHandle(quick));ApplyQuickFrame();
        UpdateMeterSchedule();
    }
    private void HideQuick(){if(!quickVisible)return;quickVisible=false;quickEntrance.Stop();quick.AppWindow.Hide();quickHiddenAt=DateTimeOffset.UtcNow;UpdateMeterSchedule();}
    private void ApplyQuickFrame()
    {
        bool highContrast=new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        uint border=highContrast?0xFFFFFFFF:0xFFFFFFFE;
        nint hwnd=WindowNative.GetWindowHandle(quick);
        const long nativeFrame=0x00C40000; // WS_CAPTION / WS_BORDER / WS_DLGFRAME / WS_THICKFRAME
        long style=(long)NativeShell.GetWindowLongPtr(hwnd,-16);
        if(quickFrameStyle==0)quickFrameStyle=style&nativeFrame;
        long desiredStyle=highContrast?style|quickFrameStyle:style&~nativeFrame;
        if(style!=desiredStyle)
        {
            NativeShell.SetWindowLongPtr(hwnd,-16,(nint)desiredStyle);
            NativeShell.SetWindowPos(hwnd,0,0,0,0,0,0x0037); // FRAMECHANGED, NOMOVE, NOSIZE, NOZORDER, NOACTIVATE
        }
        uint round=highContrast?0u:2u;NativeShell.DwmSetWindowAttribute(hwnd,33,ref round,4);
        int result=NativeShell.DwmSetWindowAttribute(hwnd,34,ref border,4);
        if(result<0)RecordDiagnostic("quick-border-unavailable");
        if(AcceptanceTrace.Enabled)
        {
            NativeShell.DwmGetWindowAttribute(hwnd,34,out uint actual,4);
            AcceptanceTrace.Record("quick-frame",AcceptanceTrace.Now,new{Result=result,Border=actual,Width=quick.AppWindow.Size.Width,ClientWidth=quick.AppWindow.ClientSize.Width,RootWidth=quickRoot.ActualWidth,Style=(long)NativeShell.GetWindowLongPtr(hwnd,-16)});
        }
    }
    private void UpdateMeterSchedule(){if(quickVisible||(fullVisible&&currentPage=="devices"))meters.Start();else meters.Stop();}
    private async Task SaveWindow()
    {
        if(full.AppWindow.Presenter is OverlappedPresenter minimized&&minimized.State==OverlappedPresenterState.Minimized)
        {await controller.SaveAsync(s=>s with{LastPage=currentPage});return;}
        var size=full.AppWindow.Size;var position=full.AppWindow.Position;
        double scale=NativeShell.GetDpiForWindow(WindowNative.GetWindowHandle(full))/96d;
        bool maximized=full.AppWindow.Presenter is OverlappedPresenter p&&p.State==OverlappedPresenterState.Maximized;
        await controller.SaveAsync(s=>s with{WindowWidth=maximized?s.WindowWidth:(int)(size.Width/scale),WindowHeight=maximized?s.WindowHeight:(int)(size.Height/scale),WindowX=maximized?s.WindowX:position.X,WindowY=maximized?s.WindowY:position.Y,Maximized=maximized,LastPage=currentPage});
    }
    public async Task QuitAsync()
    {
        if(!await ResolvePending())return;
        try{await SaveWindow();}catch(Exception ex){Error(ex);}quitting=true;meters.Stop();quickEntrance.Stop();trayMenuHost?.Dispose();systemColors.ColorValuesChanged-=colorsChanged;controller.Updated-=OnUpdate;controller.Issue-=OnIssue;controller.Diagnostic-=RecordDiagnostic;if(liveAudio is not null)liveAudio.ValuesChanged-=OnLiveValues;native.Hotkey-=hotkeys.Dispatch;hotkeys.Dispose();full.Close();quick.Close();exit();
    }
    private async Task<bool> ResolvePending()
    {
        if(savingExclusions)return false;
        if(!dirty)return true;
        var dialog=new ContentDialog{Title=T("尚有未套用的變更","Unsaved changes"),Content=T("先套用或捨棄變更，再離開目前頁面。","Apply or discard before leaving this page."),PrimaryButtonText=T("套用","Apply"),SecondaryButtonText=T("捨棄","Discard"),CloseButtonText=T("繼續編輯","Keep editing"),XamlRoot=fullRoot.XamlRoot,RequestedTheme=fullRoot.RequestedTheme};
        StyleDialog(dialog);
        var result=await dialog.ShowAsync();
        if(result==ContentDialogResult.None)return false;
        if(result==ContentDialogResult.Primary){try{if(applyPending is not null)await applyPending();}catch(Exception ex){Error(ex);return false;}}
        dirty=false;if(result==ContentDialogResult.Secondary&&currentPage=="exclusions")RenderPage();return true;
    }
    private bool UpdateValues(AudioSnapshot snapshot)
    {
        if(snapshot.ObservedAt<latestValues)return false;latestValues=snapshot.ObservedAt;
        foreach(var update in audioRows.Values.ToArray())update(snapshot);
        return true;
    }
    private VolumeControl VolumeControlFor(AudioOutput device,IReadOnlySet<string>? sessions=null)
    {
        string key=System.Text.Json.JsonSerializer.Serialize(new{Device=device.Id,Sessions=sessions?.Order(StringComparer.Ordinal).ToArray()});
        if(volumeControls.TryGetValue(key,out var existing))return existing;
        VolumeValue Value(AudioOutput output)
        {
            if(sessions is null)return new(output.Volume,output.Muted);
            var members=output.Sessions.Where(s=>sessions.Contains(s.Id)).ToArray();
            if(members.Length==0)throw new InvalidOperationException(T("音訊工作階段已結束。","Audio session ended."));
            return MixerApplications.Value(members);
        }
        var control=new VolumeControl(new VolumeReading(Value(device),controller.Snapshot.ObservedAt),v=>sessions is null?controller.Audio.SetEndpointVolumeAsync(device.Id,v):controller.Audio.SetSessionVolumeAsync(device.Id,sessions,v),m=>sessions is null?controller.Audio.SetEndpointMuteAsync(device.Id,m):controller.Audio.SetSessionMuteAsync(device.Id,sessions,m),async()=>
        {
            var snapshot=liveAudio is not null?await liveAudio.ReadValuesAsync(device.Id,sessions).ConfigureAwait(false):await controller.Audio.ReadAsync().ConfigureAwait(false);
            var actual=snapshot.Outputs.FirstOrDefault(d=>d.Id==device.Id)??throw new InvalidOperationException(T("輸出裝置已離線。","Output is offline."));
            return new VolumeReading(Value(actual),snapshot.ObservedAt);
        },action=>{dispatcher.TryEnqueue(()=>action());});volumeControls[key]=control;volumeTargets[key]=(device.Id,sessions?.ToArray());return control;
    }
    private void PruneVolumeControls()
    {
        foreach(var (key,target) in volumeTargets.ToArray())
        {
            var output=controller.Snapshot.Outputs.FirstOrDefault(d=>d.Id==target.Device);
            if(output is not null&&(target.Sessions is null||target.Sessions.All(id=>output.Sessions.Any(s=>s.Id==id))))continue;
            volumeTargets.Remove(key);volumeControls.Remove(key);
        }
    }
    private IReadOnlyList<PeakTarget> VisibleMeters(bool fullMixerVisible)
    {
        var visible=meterRows.Where(r=>(r.Key.StartsWith("quick")?quickVisible:fullMixerVisible)&&ActuallyVisible(r.Row));
        return visible.GroupBy(r=>r.Device).Select(g=>new PeakTarget(g.Key,g.SelectMany(r=>r.Sessions).ToHashSet(),g.Any(r=>r.Endpoint))).ToArray();
    }
    private static bool ActuallyVisible(FrameworkElement row)
    {
        if(!row.IsLoaded||row.ActualHeight<=0||row.XamlRoot is null)return false;
        DependencyObject? element=row;
        while(element is UIElement ui)
        {
            if(ui.Visibility!=Visibility.Visible||!ui.IsHitTestVisible)return false;
            if((ui is ScrollViewer or AnimatedReveal)&&ui is FrameworkElement clip)
            {
                var local=row.TransformToVisual(clip).TransformBounds(new global::Windows.Foundation.Rect(0,0,row.ActualWidth,row.ActualHeight));
                if(local.Bottom<=0||local.Top>=clip.ActualHeight||local.Right<=0||local.Left>=clip.ActualWidth)return false;
            }
            element=VisualTreeHelper.GetParent(element);
        }
        try
        {
            var bounds=row.TransformToVisual(null).TransformBounds(new global::Windows.Foundation.Rect(0,0,row.ActualWidth,row.ActualHeight));
            return bounds.Bottom>0&&bounds.Top<row.XamlRoot.Size.Height&&bounds.Right>0&&bounds.Left<row.XamlRoot.Size.Width;
        }
        catch(ArgumentException){return false;}
    }
    private VolumeRow EndpointRow(AudioOutput device,string key)
    {
        var row=new VolumeRow(theme,T("輸出總音量","Output volume"),VolumeControlFor(device),Error,key.StartsWith("quick"));
        meterRows.Add((key,device.Id,[],true,row));
        audioRows[key]=snap=>{if(snap.Outputs.FirstOrDefault(d=>d.Id==device.Id) is {} current)row.Update(current.Volume,current.Muted,current.Peak,snap.ObservedAt);};
        row.Update(device.Volume,device.Muted,device.Peak,controller.Snapshot.ObservedAt);return row;
    }
    private UIElement Mixer(AudioOutput device,string prefix,bool details)
    {
        var items=new StackPanel();
        var groupDetails=new StackPanel{Spacing=12};
        var sessions=prefix.StartsWith("quick")?AppExclusions.Visible(device.Sessions,controller.Settings.QuickMenuExclusions):device.Sessions;
        var groups=MixerApplications.Group(sessions);
        foreach(var group in groups)
        {
            var members=group;var ids=members.Select(s=>s.Id).ToHashSet();
            var label=Text(members[0].Name,12,true);
            var row=new VolumeRow(theme,members[0].Name,VolumeControlFor(device,ids),Error,prefix.StartsWith("quick"));
            string key=prefix+device.Id+members[0].Id;
            meterRows.Add((key,device.Id,ids,false,row));
            audioRows[key]=snap=>
            {
                var current=snap.Outputs.FirstOrDefault(d=>d.Id==device.Id)?.Sessions.Where(s=>ids.Contains(s.Id)).ToArray();if(current is not {Length:>0})return;
                var representative=current.FirstOrDefault(s=>s.Id==members[0].Id)??current[0];
                float min=current.Min(s=>s.Volume),max=current.Max(s=>s.Volume);bool allMuted=current.All(s=>s.Muted);
                label.Text=members[0].Name+(max-min>.005?$" · {T("混合","Mixed")} {min*100:0}–{max*100:0}%":"")+(current.Any(s=>s.Muted)&&!allMuted?T(" · 部分靜音"," · Partially muted"):allMuted?T(" · 已靜音"," · Muted"):"");
                float? peak=current.Any(s=>s.Peak.HasValue)?current.Max(s=>s.Muted?0:s.Peak??0):null;
                row.Update(representative.Volume,allMuted,peak,snap.ObservedAt);
                row.Describe(label.Text+(max-min>.005?T("；滑桿為固定代表值，操作才統一群組。","; slider shows a stable representative; changing it unifies current members."):""));
            };
            var app=Stack(SessionHeading(members,label),row);
            items.Children.Add(new Border{Child=app,Padding=prefix.StartsWith("quick")?new(0,7,0,10):new(16,14,16,14),BorderBrush=theme.Line,BorderThickness=new(0,items.Children.Count==0||prefix.StartsWith("quick")?0:1,0,0)});
            if(details&&members.Length>1)
            {
                var children=new StackPanel{Spacing=10};
                foreach(var session in members)
                {
                    var child=new VolumeRow(theme,session.Name,VolumeControlFor(device,new HashSet<string>{session.Id}),Error);
                    meterRows.Add((key+session.Id,device.Id,new HashSet<string>{session.Id},false,child));
                    audioRows[key+session.Id]=snap=>{if(snap.Outputs.FirstOrDefault(d=>d.Id==device.Id)?.Sessions.FirstOrDefault(s=>s.Id==session.Id) is {} value)child.Update(value.Volume,value.Muted,value.Peak,snap.ObservedAt);};
                    children.Children.Add(Stack(Text(T("播放工作階段 ","Playback session ")+(children.Children.Count+1),12,true),child));
                }
                groupDetails.Children.Add(Stack(Text(members[0].Name,12,true),children));
            }
        }
        if(groupDetails.Children.Count>0)items.Children.Add(Disclosure("sessions:"+device.Id,T("應用程式明細 · 可個別調整","Session details · adjust individually"),Inset(groupDetails)));
        if(sessions.Count==0)
        {
            string empty=device.Sessions.Count>0?T("目前應用程式皆已排除","All current apps are excluded"):T("沒有可控制的音訊應用程式","No controllable audio sessions");
            items.Children.Add(prefix.StartsWith("quick")?Text(empty,12,true):Inset(Text(empty,12,true)));
        }
        return items;
    }
    private string QuickName(AudioOutput output)=>output.IsProX2&&Name(output)==output.Name?"PRO X 2":Name(output);
    private int QuickHeight()
    {
        var device=controller.Snapshot.Outputs.FirstOrDefault(d=>d.Id==controller.Snapshot.CurrentId);
        int groups=device is null?0:MixerApplications.Group(AppExclusions.Visible(device.Sessions,controller.Settings.QuickMenuExclusions)).Count;
        bool headset=device?.Id==controller.HeadsetId&&device is not null;
        return Math.Clamp(124+(headset?20:0)+(groups==0?32:groups*69)+13+QuickFooterHeight+(int)Math.Ceiling(quickPicker?.RevealHeight??0),166+QuickFooterHeight,680);
    }
    private void SetQuickExpanded(bool value)
    {
        expanded=value;quickPicker?.SetOpen(value);
        if(quickSelector is not null)AutomationProperties.SetName(quickSelector,quickSelectorName+T(expanded?"，收合輸出裝置":"，展開輸出裝置",expanded?", collapse outputs":", expand outputs"));
    }
    private void ResizeQuick()
    {
        if(!quickVisible)return;
        double scale=NativeShell.GetDpiForWindow(WindowNative.GetWindowHandle(quick))/96d;
        var area=DisplayArea.GetFromWindowId(quick.AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
        int height=Math.Min((int)(QuickHeight()*scale),area.Height-16);var point=quick.AppWindow.Position;
        quick.AppWindow.MoveAndResize(new(point.X,area.Y+area.Height-height-8,quick.AppWindow.Size.Width,height));
    }
    private void RenderQuick()
    {
        foreach(var key in audioRows.Keys.Where(k=>k.StartsWith("quick")).ToArray())audioRows.Remove(key);
        meterRows.RemoveAll(r=>r.Key.StartsWith("quick"));quickPicker=null;
        quickRoot.Children.Clear();quickRoot.RowDefinitions.Clear();
        quickRoot.RowDefinitions.Add(new(){Height=GridLength.Auto});quickRoot.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});quickRoot.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var device=controller.Snapshot.Outputs.FirstOrDefault(d=>d.Id==controller.Snapshot.CurrentId);
        var top=new StackPanel();
        var header=new Grid{ColumnSpacing=12};header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        string name=device is null?T("沒有可用輸出","No output available"):QuickName(device);
        var nameLabel=Text(name,15);nameLabel.TextWrapping=TextWrapping.NoWrap;nameLabel.TextTrimming=TextTrimming.CharacterEllipsis;nameLabel.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var title=Actions(nameLabel);
        var selectorArrow=Icon("\uE70D",12);
        if(controller.Snapshot.Outputs.Count>1)title.Children.Add(selectorArrow);
        var select=FlatButton(name,()=>{SetQuickExpanded(!expanded);return Task.CompletedTask;});quickSelector=select;quickSelectorName=name;select.Content=title;select.Padding=new(0,4,4,4);select.HorizontalAlignment=HorizontalAlignment.Stretch;select.HorizontalContentAlignment=HorizontalAlignment.Left;select.IsEnabled=controller.Snapshot.Outputs.Count>1;
        select.SizeChanged+=(_,_)=>nameLabel.MaxWidth=Math.Max(40,select.ActualWidth-28);
        AutomationProperties.SetName(select,name+T(expanded?"，收合輸出裝置":"，展開輸出裝置",expanded?", collapse outputs":", expand outputs"));ToolTipService.SetToolTip(select,device is null?name:Name(device));
        var left=Stack(select);header.Children.Add(left);
        if(device?.Id==controller.HeadsetId&&device is not null)
        {var headsetStatus=Text("",12,true);left.Children.Add(headsetStatus);audioRows["quick-headset"]=_=>headsetStatus.Text=BatteryText()+" · "+HeadsetText();}
        var knob=new Microsoft.UI.Xaml.Shapes.Ellipse{Width=13,Height=13,Fill=new SolidColorBrush(Colors.White),Margin=new(3),HorizontalAlignment=controller.Settings.Automatic?HorizontalAlignment.Right:HorizontalAlignment.Left};
        var toggle=new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton{IsChecked=controller.Settings.Automatic,Width=34,Height=19,MinWidth=0,MinHeight=0,Padding=new(0),Background=controller.Settings.Automatic?theme.Blue:theme.Line,Content=knob,Template=(ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"ToggleButton\"><Border Background=\"{TemplateBinding Background}\" CornerRadius=\"10\"><ContentPresenter Content=\"{TemplateBinding Content}\" HorizontalContentAlignment=\"Stretch\" VerticalContentAlignment=\"Center\"/></Border></ControlTemplate>")};
        AutomationProperties.SetName(toggle,T("自動切換","Automatic switching"));
        bool syncingToggle=false;
        async void ToggleChanged(object sender,RoutedEventArgs args){if(syncingToggle)return;try{await controller.SetAutomaticAsync(toggle.IsChecked==true);}catch(Exception ex){Error(ex);}finally{syncingToggle=true;toggle.IsChecked=controller.Settings.Automatic;toggle.Background=controller.Settings.Automatic?theme.Blue:theme.Line;knob.HorizontalAlignment=controller.Settings.Automatic?HorizontalAlignment.Right:HorizontalAlignment.Left;syncingToggle=false;}}
        toggle.Checked+=ToggleChanged;toggle.Unchecked+=ToggleChanged;
        var autoLabel=Text(T("自動切換","Automatic"),12);var right=new StackPanel{Spacing=4,HorizontalAlignment=HorizontalAlignment.Right};right.Children.Add(Actions(autoLabel,toggle));
        toggle.FocusVisualPrimaryBrush=theme.Blue;toggle.FocusVisualSecondaryBrush=theme.Head;
        Grid.SetColumn(right,1);header.Children.Add(right);
        audioRows["quick-toggle"]=_=>{syncingToggle=true;toggle.IsChecked=controller.Settings.Automatic;toggle.Background=controller.Settings.Automatic?theme.Blue:theme.Line;knob.HorizontalAlignment=controller.Settings.Automatic?HorizontalAlignment.Right:HorizontalAlignment.Left;syncingToggle=false;};
        header.Margin=new(0,0,0,12);top.Children.Add(header);
        if(controller.Snapshot.Outputs.Count>1)
        {
            var list=new StackPanel{Spacing=4,Padding=new(6)};
            foreach(var output in controller.Snapshot.Outputs.OrderByDescending(d=>d.Id==device?.Id).ThenBy(Name))
            {
                bool chosen=output.Id==device?.Id;
                var labels=Stack(Text(QuickName(output)),Text(DeviceState(output),12,true));
                var optionContent=new Grid{ColumnSpacing=8};optionContent.ColumnDefinitions.Add(new(){Width=new(20)});optionContent.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});optionContent.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                optionContent.Children.Add(Icon(chosen?"\uE73E":DeviceGlyph(output),16));Grid.SetColumn(labels,1);optionContent.Children.Add(labels);
                if(output.Id==controller.HeadsetId){var battery=Text(BatteryText(),12,true);Grid.SetColumn(battery,2);optionContent.Children.Add(battery);audioRows["quick-option-battery"]=_=>battery.Text=BatteryText();}
                var option=FlatButton(Name(output),async()=>{await controller.ManualSwitchAsync(output.Id);expanded=false;RenderQuick();});option.Content=optionContent;option.Padding=new(8,10,8,10);option.Background=chosen?theme.Hover:new SolidColorBrush(Colors.Transparent);option.HorizontalAlignment=HorizontalAlignment.Stretch;option.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                list.Children.Add(option);
            }
            var pickerContent=new Border{Child=Card(new ScrollViewer{Content=list,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}),Padding=new(0,0,0,12)};
            quickPicker=new AnimatedReveal(pickerContent,expanded,selectorArrow,()=>systemColors.AnimationsEnabled,ResizeQuick);
            top.Children.Add(quickPicker);
        }
        if(device is not null)top.Children.Add(Stack(Text(T("輸出總音量","Output volume"),12,true),EndpointRow(device,"quick-total")));
        quickRoot.Children.Add(new Border{Child=top,Background=theme.Head,Padding=new(18,16,18,18),BorderBrush=theme.Line,BorderThickness=new(1,1,1,1),CornerRadius=new(12,12,0,0)});
        var body=new StackPanel{Spacing=4,Padding=new(18,5,18,8)};
        if(device is not null)body.Children.Add(Mixer(device,"quick",false));else body.Children.Add(Text(T("等待輸出裝置恢復","Waiting for an output device"),12,true));
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var bottom=new Border{Child=scroll,Background=theme.Panel,BorderBrush=theme.Line,BorderThickness=new(1,0,1,0)};Grid.SetRow(bottom,1);quickRoot.Children.Add(bottom);
        var openPanel=FlatButton(T("開啟面板","Open panel"),()=>{ShowFull();return Task.CompletedTask;});
        openPanel.Content=Actions(Icon("\uE8A7",16),Text(T("開啟面板","Open panel"),13));openPanel.HorizontalAlignment=HorizontalAlignment.Stretch;openPanel.HorizontalContentAlignment=HorizontalAlignment.Left;openPanel.VerticalAlignment=VerticalAlignment.Stretch;openPanel.Padding=new(18,8,18,8);openPanel.MinHeight=0;
        var footer=new Border{Height=QuickFooterHeight,Child=openPanel,Background=theme.Head,BorderBrush=theme.Line,BorderThickness=new(1,1,1,1),CornerRadius=new(0,0,12,12)};Grid.SetRow(footer,2);quickRoot.Children.Add(footer);
        ResizeQuick();
        UpdateValues(controller.Snapshot);
    }
}
