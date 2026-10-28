using AudioOutputManager.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using WinRT.Interop;

namespace AudioOutputManager.App;

/// <summary>Each flyout owns a fresh transparent anchor; hidden WinUI roots are never reused.</summary>
internal sealed class TrayMenuHost : IDisposable
{
    private readonly TrayMenuLifetime lifetime=new();
    private Action? closeActive;
    private bool disposed;
    public async Task<TrayMenuCommand> ShowAsync(string openPanel,string exit)
    {
        if(disposed)return TrayMenuCommand.None;
        // A new shell request replaces a dismissed/pending flyout even if its Closed event was lost.
        Hide();
        var pending=lifetime.TryBegin()!;
        Window? host=null;
        Grid? anchor=null;
        bool closingHost=false,loadedSubscribed=false;
        MenuFlyout? popup=null;
        TrayMenuCommand selected=TrayMenuCommand.None;
        var loaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object sender,RoutedEventArgs args)=>loaded.TrySetResult();
        void Cleanup()
        {
            closeActive=null;
            if(loadedSubscribed){anchor!.Loaded-=Loaded;loadedSubscribed=false;}
            try
            {
                if(popup is not null)
                {
                    popup.Closed-=Closed;
                    // A left-click handoff must finish closing before quick receives focus.
                    if(popup.IsOpen){popup.AreOpenCloseAnimationsEnabled=false;popup.Hide();}
                }
            }
            finally
            {
                // A hidden host can retain XamlRoot while never raising Loaded on Show again.
                // Destroy this request's window; the next request gets an independent root.
                if(host is not null){closingHost=true;host.Close();}
            }
        }
        void Close()=>lifetime.Complete(pending,selected,Cleanup);
        void Closed(object? sender,object args)=>Close();
        closeActive=()=>lifetime.Complete(pending,TrayMenuCommand.None,Cleanup);
        try
        {
            host=new(){Title="AuriHelm · Commands"};
            anchor=new(){Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
            host.Content=anchor;host.ExtendsContentIntoTitleBar=true;host.AppWindow.IsShownInSwitchers=false;
            if(host.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;
            }
            nint hwnd=WindowNative.GetWindowHandle(host);
            NativeShell.SetWindowLongPtr(hwnd,-20,(nint)(((long)NativeShell.GetWindowLongPtr(hwnd,-20)|0x80080)&~0x40000));
            if(!NativeShell.SetLayeredWindowAttributes(hwnd,0,0,2))throw new System.ComponentModel.Win32Exception();
            host.AppWindow.Resize(new(1,1));
            host.AppWindow.Closing+=(_,args)=>{if(!closingHost){args.Cancel=true;closeActive?.Invoke();}};
            anchor.Loaded+=Loaded;loadedSubscribed=true;
            if(!NativeShell.GetCursorPos(out var point))return TrayMenuCommand.None;
            var area=new NativeShell.MonitorInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeShell.MonitorInfo>()};
            if(!NativeShell.GetMonitorInfo(NativeShell.MonitorFromPoint(point,2),ref area))return TrayMenuCommand.None;
            host.AppWindow.Move(new(Math.Clamp(point.X,area.Work.Left,area.Work.Right-1),Math.Clamp(point.Y,area.Work.Top,area.Work.Bottom-1)));
            using(var personalization=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                anchor.RequestedTheme=new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast?ElementTheme.Default:
                    personalization?.GetValue("SystemUsesLightTheme") is int value&&value!=0?ElementTheme.Light:ElementTheme.Dark;
            host.AppWindow.Show();host.Activate();NativeShell.SetForegroundWindow(WindowNative.GetWindowHandle(host));
            // Hide/left-click also cancels a request that is still waiting for the XAML anchor.
            if(!anchor.IsLoaded)
            {
                await Task.WhenAny(loaded.Task,pending.Result).WaitAsync(TimeSpan.FromSeconds(3));
            }
            if(disposed||!lifetime.IsCurrent(pending))return TrayMenuCommand.None;
            popup=new(){ShouldConstrainToRootBounds=false,ShowMode=FlyoutShowMode.Standard,SystemBackdrop=new DesktopAcrylicBackdrop()};
            foreach(var command in TrayMenu.Items(openPanel,exit))
            {
                var item=new MenuFlyoutItem{Text=command.Text,Icon=new FontIcon{Glyph=command.Glyph}};
                item.Click+=(_,_)=>selected=command.Command;popup.Items.Add(item);
            }
            popup.Closed+=Closed;
            popup.ShowAt(anchor,new FlyoutShowOptions{Position=new(1,0),Placement=FlyoutPlacementMode.TopEdgeAlignedRight,ShowMode=FlyoutShowMode.Standard});
            return await pending.Result;
        }
        finally
        {
            if(loadedSubscribed){anchor!.Loaded-=Loaded;loadedSubscribed=false;}
            // An old continuation must never hide the host after quick/new menu activation.
            lifetime.Release(pending,Cleanup);
        }
    }
    public void Hide(){if(!disposed)closeActive?.Invoke();}
    public void Dispose(){if(disposed)return;Hide();disposed=true;}
}
