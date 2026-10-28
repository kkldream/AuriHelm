using System.Runtime.InteropServices;
using AudioOutputManager.Windows;
using Microsoft.Win32;

namespace AudioOutputManager.App;

internal sealed class NativeShell : IDisposable
{
    private readonly WndProc procedure;
    private readonly string className="AudioOutputManager.Host."+Guid.NewGuid().ToString("N");
    private readonly uint taskbarCreated;
    private readonly TrayInteraction interaction;
    public nint Handle {get;}
    public event Action? TrayClicked;
    public event Action? TrayContextRequested;
    public event Action<int>? Hotkey;
    public event Action<bool>? PowerChanged;
    private bool disposed;
    private BrandIcon? trayIcon;
    private (int Size,uint Color)? trayStyle;
    public NativeShell()
    {
        interaction=new(()=>TrayClicked?.Invoke(),()=>TrayContextRequested?.Invoke());
        procedure=Process;
        var wc=new WndClass { Procedure=Marshal.GetFunctionPointerForDelegate(procedure),ClassName=className,Instance=GetModuleHandle(null) };
        if(RegisterClass(ref wc)==0) throw new System.ComponentModel.Win32Exception();
        Handle=CreateWindowEx(0x80,className,"AuriHelm Host",0,0,0,0,0,0,0,wc.Instance,0);
        if(Handle==0) throw new System.ComponentModel.Win32Exception();
        taskbarCreated=RegisterWindowMessage("TaskbarCreated"); AddIcon();
    }
    private nint Process(nint hwnd,uint message,nint wparam,nint lparam)
    {
        if(message==0x8001 && interaction.Dispatch(unchecked((uint)(long)lparam)))return 0;
        if(message==0x312) { Hotkey?.Invoke((int)wparam); return 0; }
        if(message==0x218 && (int)wparam==4)PowerChanged?.Invoke(true);
        if(message==0x218 && ((int)wparam is 7 or 18))PowerChanged?.Invoke(false);
        if(message==taskbarCreated && taskbarCreated!=0) AddIcon();
        if(message is 0x001A or 0x031A or 0x02E0 && Handle!=0)UpdateIcon();
        return DefWindowProc(hwnd,message,wparam,lparam);
    }
    private void AddIcon()
    {
        EnsureIcon();
        var data=IconData(); data.Flags=7;data.Callback=0x8001;data.Icon=trayIcon!.DangerousGetHandle();data.Tip="AuriHelm";
        if(!ShellNotifyIcon(0,ref data)) throw new System.ComponentModel.Win32Exception();
    }
    private bool EnsureIcon()
    {
        var contrast=new HighContrast{Size=(uint)Marshal.SizeOf<HighContrast>()};
        bool highContrast=SystemParametersInfo(0x42,contrast.Size,ref contrast,0)&&(contrast.Flags&1)!=0;
        using var personalization=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
        bool light=personalization?.GetValue("SystemUsesLightTheme") is int value&&value!=0;
        uint system=GetSysColor(8);
        uint color=highContrast?((system&255)<<16)|(system&0xFF00)|((system>>16)&255):light?0x202B36u:0xEFF6FFu;
        int size=Math.Max(16,GetSystemMetricsForDpi(49,GetDpiForSystem()));
        if(trayIcon is not null&&trayStyle==(size,color))return false;
        var next=BrandIcon.Load(Path.Combine(AppContext.BaseDirectory,"Assets","AuriHelm.Tray.ico"),size,color);
        var previous=trayIcon;trayIcon=next;trayStyle=(size,color);previous?.Dispose();return true;
    }
    private void UpdateIcon()
    {
        if(!EnsureIcon())return;
        var data=IconData();data.Flags=2;data.Icon=trayIcon!.DangerousGetHandle();ShellNotifyIcon(1,ref data);
    }
    private NotifyIconData IconData()=>new() { Size=(uint)Marshal.SizeOf<NotifyIconData>(),Window=Handle,Id=1,Tip="",Info="",InfoTitle="" };
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        for(int i=1;i<=3;i++) UnregisterHotKey(Handle,i);
        var data=IconData();ShellNotifyIcon(2,ref data);trayIcon?.Dispose();DestroyWindow(Handle);UnregisterClass(className,GetModuleHandle(null));
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct WndClass { public uint Style;public nint Procedure;public int ClassExtra,WindowExtra;public nint Instance,Icon,Cursor,Background;public string? MenuName;public string ClassName; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct NotifyIconData { public uint Size;public nint Window;public uint Id,Flags,Callback;public nint Icon;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;public uint Timeout;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;public uint InfoFlags;public Guid Guid;public nint BalloonIcon; }
    private delegate nint WndProc(nint hwnd,uint message,nint wparam,nint lparam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern ushort RegisterClass(ref WndClass wc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint CreateWindowEx(uint ex,string cls,string name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")]private static extern nint DefWindowProc(nint hwnd,uint message,nint wparam,nint lparam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string name);
    [StructLayout(LayoutKind.Sequential)]private struct HighContrast{public uint Size,Flags;public nint Scheme;}
    [DllImport("user32.dll",EntryPoint="SystemParametersInfoW")]private static extern bool SystemParametersInfo(uint action,uint size,ref HighContrast value,uint flags);
    [DllImport("user32.dll")]private static extern uint GetSysColor(int index);
    [DllImport("user32.dll")]private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]private static extern int GetSystemMetricsForDpi(int index,uint dpi);
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message,ref NotifyIconData data);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool UnregisterClass(string cls,nint instance);
    [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll")]internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]internal static extern nint MonitorFromPoint(Point point,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")]internal static extern int DwmSetWindowAttribute(nint hwnd,uint attribute,ref uint value,uint size);
    [DllImport("dwmapi.dll")]internal static extern int DwmGetWindowAttribute(nint hwnd,uint attribute,out uint value,uint size);
    [DllImport("user32.dll",SetLastError=true)]internal static extern bool SetWindowPos(nint hwnd,nint insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]internal static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]internal static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32.dll")]internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetLayeredWindowAttributes(nint hwnd,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")]internal static extern short GetKeyState(int key);
    [StructLayout(LayoutKind.Sequential)]internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)]internal struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)]internal struct MonitorInfo { public uint Size;public Rect Monitor,Work;public uint Flags; }
}
