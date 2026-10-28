using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class PolicyTests
{
    private readonly DateTimeOffset start=DateTimeOffset.Parse("2026-10-18T00:00:00Z");
    [Fact] public void UnknownBreaksOffContinuity()
    {
        var p=new AutomaticPolicy();
        void Observe(HeadsetState state,double seconds) { var now=start.AddSeconds(seconds); p.Observe(new(state,now),now,2); }
        Observe(HeadsetState.Off,0); Observe(HeadsetState.Off,1); Observe(HeadsetState.Unknown,1.5); Observe(HeadsetState.Off,2); Observe(HeadsetState.Off,3);
        Assert.Equal(HeadsetState.Unknown,p.Confirmed);
        Observe(HeadsetState.Off,4); Assert.Equal(HeadsetState.Off,p.Confirmed);
    }
    [Fact] public void DuplicateAndStaleSamplesCannotConfirm()
    {
        var p=new AutomaticPolicy(); var r=new HeadsetReading(HeadsetState.On,start);
        p.Observe(r,start,2); p.Observe(r,start,2); Assert.Equal(HeadsetState.Unknown,p.Confirmed);
        p.Observe(new(HeadsetState.On,start.AddSeconds(1)),start.AddSeconds(5),2); Assert.Equal(HeadsetState.Unknown,p.Confirmed);
    }
    [Fact] public void HigherUnknownHeadsetDoesNotStealOrChooseFallbackUntilCurrentFails()
    {
        var p=new AutomaticPolicy();
        AudioSnapshot Snapshot(bool speaker) => new([new("headset","耳機",true,.5f,false,null,[]),new("speaker","喇叭",speaker,.5f,false,null,[]),new("other","未加入",true,.5f,false,null,[])],new Dictionary<AudioRole,string?> { [AudioRole.Console]="speaker",[AudioRole.Multimedia]="speaker",[AudioRole.Communications]="speaker" },start);
        var settings=new AppSettings {Automatic=true,Priority=["headset","speaker"]};
        Assert.Null(p.Choose(settings,Snapshot(true),"headset",false));
        Assert.Null(p.Choose(settings,Snapshot(false),"headset",false));
        Assert.Null(p.Choose(settings with {Priority=[]},Snapshot(true),"headset",false));
    }
    [Fact] public void NextUsesOnlyPriorityAndDoesNotRechooseOnlyCurrent()
    {
        Assert.Equal("b",AutomaticPolicy.Next(["a","b"],["a","b","outside"],"a"));
        Assert.Null(AutomaticPolicy.Next(["a","b"],["a","outside"],"a"));
        Assert.Equal("a",AutomaticPolicy.Next(["a","b"],["a","b"],"outside"));
        Assert.Null(AutomaticPolicy.Next([], ["outside"],"outside"));
    }
    [Fact] public void SettingsBackupAndUnknownVersionAreProtected()
    {
        string directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try
        {
            var store=new SettingsStore(directory); store.Save(new()); store.Save(new() {Priority=["a"]});
            File.WriteAllText(Path.Combine(directory,"settings.json"),"broken");
            var load=store.Load(); Assert.NotNull(load.Warning); Assert.False(load.Settings.Automatic);
            File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"Version\":999}");
            Assert.Throws<NotSupportedException>(()=>store.Load());
            Assert.Throws<NotSupportedException>(()=>store.Save(new()));
            Assert.Contains("999",File.ReadAllText(Path.Combine(directory,"settings.json")));
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
}
