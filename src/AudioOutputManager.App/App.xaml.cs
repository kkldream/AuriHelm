using Microsoft.UI.Xaml;
using AudioOutputManager.Windows;

namespace AudioOutputManager.App;

public partial class App : Application
{
    private SingleInstance? instance;
    private NativeShell? tray;
    private AppController? controller;
    private ShellWindow? shell;
    public App() { InitializeComponent(); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            instance=new();
            bool requestExit=Environment.GetCommandLineArgs().Contains("--request-exit");
            if(!instance.IsPrimary){await instance.SignalAsync(requestExit?(byte)2:(byte)1);instance.Dispose();Exit();return;}
            if(requestExit){instance.Dispose();Exit();return;}
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AudioOutputManager");
            controller=new(directory);await controller.StartAsync();
            tray=new();shell=new(controller,tray,Shutdown,directory);
            tray.PowerChanged+=async sleeping=>{try{await controller.PowerChangedAsync(sleeping);}catch(OperationCanceledException){}};
            tray.TrayClicked+=shell.ToggleQuick;
            tray.TrayContextRequested+=shell.ShowTrayMenu;
            var dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            instance.OpenRequested+=()=>dispatcher.TryEnqueue(()=>shell.ShowFull());
            instance.ExitRequested+=()=>dispatcher.TryEnqueue(async()=>await shell.QuitAsync());
            instance.StartListener();
            if(!Environment.GetCommandLineArgs().Contains("--background"))shell.ShowFull();
        }
        catch(Exception)
        {
            // Startup failures are local; never upload paths or exception payloads.
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AudioOutputManager","logs");
            try{new LocalDiagnostics(directory).Record("startup-failed");}catch(IOException){}
            Shutdown();
        }
    }
    private void Shutdown(){tray?.Dispose();controller?.Dispose();instance?.Dispose();Exit();}
}
