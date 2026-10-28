using AudioOutputManager.Core;
using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class IconPipelineTests
{
    [Fact] public async Task ConcurrentViewsShareWorkAndFailureCanRecoverAfterBackoff()
    {
        var now=DateTimeOffset.UtcNow;var cache=new IconCache<string>(()=>now);int calls=0;
        var read=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string?> Load(){calls++;return read.Task;}
        var a=cache.GetAsync("exe:size:version","session1",Load);var b=cache.GetAsync("exe:size:version","session1",Load);
        Assert.Equal(1,calls);read.SetResult(null);Assert.True((await a).Retry);Assert.True((await b).Retry);
        Assert.Null((await cache.GetAsync("exe:size:version","session1",Load)).Value);Assert.Equal(1,calls);
        now=now.AddSeconds(5);
        var recovered=await cache.GetAsync("exe:size:version","session1",()=>{calls++;return Task.FromResult<string?>("png");});
        Assert.Equal("png",recovered.Value);Assert.False(recovered.Retry);Assert.Equal(2,calls);
        Assert.Equal("png",(await cache.GetAsync("exe:size:version","new-session",()=>throw new Exception("must remain cached"))).Value);
    }
    [Fact] public async Task FailuresHaveFiniteBudgetAndNewSessionOrFileVersionCanRetry()
    {
        var now=DateTimeOffset.UtcNow;var cache=new IconCache<string>(()=>now);int calls=0;
        Task<string?> Fail(){calls++;return Task.FromResult<string?>(null);}
        for(int i=0;i<3;i++){var failed=await cache.GetAsync("file-v1","s1",Fail);Assert.Equal(i<2,failed.Retry);now=now.AddSeconds(5);}
        Assert.False((await cache.GetAsync("file-v1","s1",Fail)).Retry);Assert.Equal(3,calls);
        Assert.True((await cache.GetAsync("file-v1","s2",Fail)).Retry);Assert.Equal(4,calls);
        Assert.Equal("new",(await cache.GetAsync("file-v2","s2",()=>Task.FromResult<string?>("new"))).Value);
    }
    [Fact] public async Task LateOldImageCannotReplaceCurrentAppAndTemporaryFailureKeepsLastGood()
    {
        var slot=new LatestIcon<string>();var old=new TaskCompletionSource<string?>();
        var pending=slot.UpdateAsync("old",()=>old.Task);
        Assert.True(await slot.UpdateAsync("new",()=>Task.FromResult<string?>("new-icon")));
        old.SetResult("old-icon");Assert.False(await pending);Assert.Equal("new-icon",slot.Value);
        Assert.True(await slot.UpdateAsync("new",()=>Task.FromResult<string?>(null)));Assert.Equal("new-icon",slot.Value);
        var delayed=new TaskCompletionSource<string?>();var hidden=slot.UpdateAsync("new",()=>delayed.Task);slot.Cancel();delayed.SetResult("late");Assert.False(await hidden);
        await slot.UpdateAsync("different",()=>Task.FromResult<string?>(null));Assert.Null(slot.Value);
    }
    [Fact] public void RequestUsesAllKnownSourcesWithoutChangingAppIdentityOrExclusions()
    {
        var a=new AudioSession("1","app","exe:C:\\Apps\\app.exe",Guid.NewGuid(),.5f,false,null);
        var b=a with{Id="2",IconFile="C:\\Apps\\app.exe",IconPath="\"C:\\Apps\\icons.dll\",-2"};
        var request=AppIconRequest.Create([a,b],48);
        Assert.Equal(AppExclusions.Key(a.AppIdentity),request.Identity);Assert.Equal(48,request.Size);
        Assert.Equal(new[]{AppIconSourceKind.Resource,AppIconSourceKind.Executable,AppIconSourceKind.Thumbnail,AppIconSourceKind.Thumbnail},request.Sources.Select(s=>s.Kind));
        Assert.Equal(b.IconPath,request.Sources[0].Value);
        Assert.Equal(2,AppExclusions.Visible([a,b],new Dictionary<string,string>()).Count);
        var packaged=a with{AppIdentity="aumid:Example.Package!App",IconFile=null};
        Assert.Equal(AppIconSourceKind.Package,AppIconRequest.Create([packaged],32).Sources[0].Kind);
        Assert.True(AppIconRequest.Create([a with{AppIdentity=AppExclusions.SystemSounds}],32).SystemSounds);
    }
    [Fact] public void IconOnlyMetadataRefreshesViewsWithoutLosingDraft()
    {
        var session=new AudioSession("1","app","exe:C:\\Apps\\app.exe",Guid.Empty,.4f,false,null);
        AudioSnapshot Snapshot(AudioSession s)=>new([new("d","device",true,.5f,false,null,[s])],Enum.GetValues<AudioRole>().ToDictionary(r=>r,_=>(string?)"d"),DateTimeOffset.UtcNow);
        var tracker=new ViewRefreshTracker();var draft=new AppExclusionDraft(new Dictionary<string,string>());var original=Snapshot(session);
        draft.Search="app";draft.Set(Assert.Single(draft.Candidates(original)),true);
        tracker.Observe(original,new(),null,"exclusions",true);
        var updated=Snapshot(session with{IconPath="C:\\Apps\\icons.dll,1"});var plan=tracker.Observe(updated,new(),null,"exclusions",true);
        Assert.True(plan.Full);Assert.True(plan.Quick);Assert.True(plan.PreserveDraft);Assert.Equal("app",draft.Search);Assert.True(draft.IsDirty);Assert.True(draft.Filter(draft.Candidates(updated)).Single().Detected);
        var notified=updated.Outputs[0].Sessions[0] with{IconRevision=1};
        plan=tracker.Observe(Snapshot(notified),new(),null,"exclusions",true);
        Assert.True(plan.Full);Assert.NotEqual(AppIconRequest.Create(updated.Outputs[0].Sessions,32).Revision,AppIconRequest.Create([notified],32).Revision);
    }
    [Theory]
    [InlineData("https://example.com/icon.png")][InlineData("shell:AppsFolder\\App")][InlineData("\\\\server\\share\\icon.ico")]
    [InlineData("\\\\?\\C:\\Apps\\icon.ico")][InlineData("C:icon.ico")][InlineData("C:\\Apps\\icon.exe:stream")][InlineData("C:\\Apps\\icon.txt")]
    public void UntrustedIconLocationNeverReachesShell(string source)=>Assert.Null(AppIconReader.ParseLocalResource(source));
    [Fact] public void LocalResourceSupportsQuotedCommaPathAndNegativeResourceId()
    {
        var source=AppIconReader.ParseLocalResource("@\"C:\\Apps, Inc\\icons.dll\",-42");
        Assert.NotNull(source);Assert.Equal("C:\\Apps, Inc\\icons.dll",source.Path);Assert.Equal(-42,source.Index);
    }
    [Fact] public async Task ActualLocalIconIsPngAndMissingFileCanRecoverWithoutRestart()
    {
        string folder=Path.Combine(Path.GetTempPath(),"AuriHelm-IconTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string file=Path.Combine(folder,"fixture.ico");var reader=new AppIconReader();
            var request=new AppIconRequest("fixture","s1",32,[new(AppIconSourceKind.Resource,file)]);
            Assert.Null((await reader.ReadAsync(request)).Value);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets","AuriHelm.Tray.ico"),file);
            var result=await reader.ReadAsync(request);Assert.NotNull(result.Value);Assert.Equal("Resource",result.Value.Source);
            Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},result.Value.Png.Take(8));
            Assert.Same(result.Value,(await reader.ReadAsync(request)).Value);
            Assert.NotSame(result.Value,(await reader.ReadAsync(request with{SourceRevision=1})).Value);
            using var encoded=new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            using(var writer=new global::Windows.Storage.Streams.DataWriter(encoded.GetOutputStreamAt(0))){writer.WriteBytes(result.Value.Png);await writer.StoreAsync();}
            encoded.Seek(0);var decoded=await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(encoded);
            Assert.Equal(32u,decoded.PixelWidth);Assert.Equal(32u,decoded.PixelHeight);
            var data=(await decoded.GetPixelDataAsync(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,new(),global::Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,global::Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
            Assert.Equal(0,data[3]);Assert.Contains(Enumerable.Range(0,32*32),i=>data[i*4+3]>0);
            string png=Path.Combine(folder,"explicit.png");File.WriteAllBytes(png,result.Value.Png);
            var explicitFirst=new AppIconRequest("fixture","s1",32,[new(AppIconSourceKind.Resource,png),new(AppIconSourceKind.Executable,"C:\\Windows\\not-an-app.exe")]);
            Assert.Equal("Resource",(await reader.ReadAsync(explicitFirst)).Value!.Source);
        }
        finally
        {
            string full=Path.GetFullPath(folder),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Path.GetDirectoryName(full)!=temp||!Path.GetFileName(full).StartsWith("AuriHelm-IconTest-",StringComparison.Ordinal))throw new IOException("拒絕清理非測試目錄。");
            Directory.Delete(full,true);
        }
    }
}
