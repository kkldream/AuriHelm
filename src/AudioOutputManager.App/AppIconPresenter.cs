using AudioOutputManager.Core;
using AudioOutputManager.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace AudioOutputManager.App;

internal sealed class AppIconPresenter : Grid
{
    private readonly AppIconReader reader;
    private readonly Image image=new(){Width=18,Height=18,Visibility=Visibility.Collapsed};
    private readonly FontIcon fallback=new(){FontSize=16};
    private readonly LatestIcon<BitmapImage> latest=new();
    private readonly DispatcherTimer retry=new(){Interval=TimeSpan.FromSeconds(5)};
    private AudioSession[] sessions=[];
    private string? requestKey,identity;
    public AppIconPresenter(AppIconReader reader,Brush foreground)
    {
        this.reader=reader;Width=20;Height=20;fallback.Foreground=foreground;Children.Add(fallback);Children.Add(image);
        Loaded+=(_,_)=>Refresh(true);Unloaded+=(_,_)=>{retry.Stop();latest.Cancel();requestKey=null;};
        SizeChanged+=(_,_)=>Refresh(false);
        retry.Tick+=(_,_)=>{retry.Stop();Refresh(true);};
    }
    public void Update(IEnumerable<AudioSession> value){sessions=value.ToArray();Refresh(false);}
    private async void Refresh(bool force)
    {
        if(!IsLoaded||sessions.Length==0)return;
        var request=AppIconRequest.Create(sessions,(int)Math.Ceiling(18*(XamlRoot?.RasterizationScale??1)));
        string key=request.Key+"|"+request.Revision;
        if(!force&&key==requestKey)return;
        requestKey=key;retry.Stop();
        fallback.Glyph=request.SystemSounds?"\uE8D6":"\uE737";
        if(identity!=request.Identity){identity=request.Identity;image.Source=null;image.Visibility=Visibility.Collapsed;fallback.Visibility=Visibility.Visible;}
        bool again=false;
        try
        {
            bool current=await latest.UpdateAsync(request.Identity,async()=>
            {
                var result=await reader.ReadAsync(request);again=result.Retry;
                if(result.Value is not {} value)return null;
                using var stream=new InMemoryRandomAccessStream();
                using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(value.Png);await writer.StoreAsync();}
                stream.Seek(0);var source=new BitmapImage();await source.SetSourceAsync(stream);return source;
            });
            if(!current||!IsLoaded)return;
            if(latest.Value is {} bitmap){image.Source=bitmap;image.Visibility=Visibility.Visible;fallback.Visibility=Visibility.Collapsed;}
            if(again)retry.Start();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // Icon failure must never interrupt volume controls or show an application error dialog.
        }
    }
}
