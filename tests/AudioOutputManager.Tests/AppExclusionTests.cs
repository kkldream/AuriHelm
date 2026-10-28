using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class AppExclusionTests
{
    [Fact] public void LaterSessionIconCanRepresentTheSameApplication()
    {
        var first=Session("first",Chrome);
        var second=Session("second",Chrome) with{IconFile="C:\\Apps\\Chrome\\chrome.exe"};
        var draft=new AppExclusionDraft(new Dictionary<string,string>());
        var app=Assert.Single(draft.Candidates(Snapshot(Output("a",first,second))));
        Assert.Equal(second.IconFile,app.Session!.IconFile);
        Assert.Equal(2,app.Groups);
    }
    [Fact] public async Task DisappearingDraftNeverClaimsToBeSavedBeforeSuccessfulApply()
    {
        var draft=new AppExclusionDraft(new Dictionary<string,string>{{AppExclusions.SystemSounds,"系統音效"}});
        var app=Assert.Single(draft.Candidates(Snapshot(Output("a",Session("fixture",Chrome)))),a=>a.Detected);
        draft.Set(app,true);
        var offline=draft.Candidates(Snapshot());
        Assert.True(offline.Single(a=>a.Key==AppExclusions.SystemSounds).Saved);
        Assert.False(offline.Single(a=>a.Key==app.Key).Saved);
        await Assert.ThrowsAsync<IOException>(()=>draft.ApplyAsync(_=>throw new IOException("locked")));
        Assert.False(draft.Candidates(Snapshot()).Single(a=>a.Key==app.Key).Saved);
        await draft.ApplyAsync(_=>Task.CompletedTask);
        Assert.All(draft.Candidates(Snapshot()),a=>Assert.True(a.Saved));
    }
    private static AudioSession Session(string id,string identity,string name="chrome",Guid? group=null)=>new(id,name,identity,group??Guid.NewGuid(),.7f,false,null);
    private static AudioOutput Output(string id,params AudioSession[] sessions)=>new(id,id,true,.5f,false,null,sessions);
    private static AudioSnapshot Snapshot(params AudioOutput[] outputs)=>new(outputs,Enum.GetValues<AudioRole>().ToDictionary(r=>r,_=>(string?)"a"),DateTimeOffset.UtcNow);
    private const string Chrome="exe:C:\\Apps\\Chrome\\chrome.exe";

    [Fact] public async Task StableIdentityHidesEveryGroupOutputAndNewSessionWithoutChangingSourceMixer()
    {
        var original=Snapshot(Output("a",Session("1",Chrome),Session("2",Chrome),Session("3","exe:C:\\Other\\chrome.exe")),Output("b",Session("4","exe:c:/apps/chrome/./chrome.exe")));
        var draft=new AppExclusionDraft(new Dictionary<string,string>());
        var app=Assert.Single(draft.Candidates(original),a=>a.Key==AppExclusions.Key(Chrome));
        Assert.Equal(3,app.Groups);draft.Set(app,true);
        Dictionary<string,string>? saved=null;
        await draft.ApplyAsync(next=>{saved=next;return Task.CompletedTask;});
        Assert.Equal("3",Assert.Single(AppExclusions.Visible(original.Outputs[0].Sessions,saved!)).Id);
        Assert.Empty(AppExclusions.Visible(original.Outputs[1].Sessions,saved!));
        Assert.Empty(AppExclusions.Visible([Session("new",Chrome)],saved!));
        Assert.Equal(3,original.Outputs[0].Sessions.Count);Assert.Equal(.7f,original.Outputs[0].Sessions[0].Volume);
        Assert.Equal(2,draft.Candidates(original).Count); // same name, distinct paths
    }

    [Fact] public void SystemSoundsHasStableIdentityAndSessionFallbackCannotBeExcluded()
    {
        var draft=new AppExclusionDraft(new Dictionary<string,string>());
        var snapshot=Snapshot(Output("a",Session("sound",AppExclusions.SystemSounds,"系統音效"),Session("unknown","session:changing","未知")));
        var apps=draft.Candidates(snapshot);
        draft.Set(apps.Single(a=>a.Key is null),true);Assert.Equal(0,draft.Count);
        draft.Set(apps.Single(a=>a.Key==AppExclusions.SystemSounds),true);Assert.Equal(1,draft.Count);
        Assert.Equal("aumid:VENDOR.APP!MAIN",AppExclusions.Key("AUMID:Vendor.App!main"));
        Assert.Null(AppExclusions.Key("exe:relative.exe"));Assert.Null(AppExclusions.Key("pid:123"));
    }

    [Fact] public async Task DraftSurvivesRefreshSearchAndFailedSaveAndOfflineExclusionCanBeRemoved()
    {
        string key=AppExclusions.Key(Chrome)!;var persisted=new Dictionary<string,string>{{key,"chrome"}};
        var draft=new AppExclusionDraft(persisted){Search="chrom",OnlyExcluded=true};
        var offline=Assert.Single(draft.Filter(draft.Candidates(Snapshot())));Assert.False(offline.Detected);
        draft.Set(offline,false);Assert.True(draft.IsDirty);Assert.Empty(draft.Filter(draft.Candidates(Snapshot())));
        Assert.Single(draft.Candidates(Snapshot())); // retained until apply
        await Assert.ThrowsAsync<IOException>(()=>draft.ApplyAsync(_=>throw new IOException("failure")));
        Assert.True(draft.IsDirty);Assert.Single(persisted);Assert.Equal("chrom",draft.Search);Assert.True(draft.OnlyExcluded);
        await draft.ApplyAsync(next=>{persisted=next;return Task.CompletedTask;});Assert.Empty(persisted);Assert.False(draft.IsDirty);
        Assert.Empty(draft.Candidates(Snapshot()));
    }

    [Fact] public async Task NewAppsStayVisibleAndPathChangesDoNotInheritAnOldExclusion()
    {
        string key=AppExclusions.Key(Chrome)!;var draft=new AppExclusionDraft(new Dictionary<string,string>{{key,"chrome"}});
        var current=Snapshot(Output("a",Session("new","exe:C:\\AppsV2\\chrome.exe")));
        var apps=draft.Candidates(current);Assert.Equal(2,apps.Count);Assert.Single(apps,a=>!a.Detected);
        Assert.False(draft.IsExcluded(apps.Single(a=>a.Detected).Key));
        draft.Set(apps.Single(a=>a.Detected),true);Assert.Equal(2,draft.Count);
        Assert.Equal(2,draft.Candidates(Snapshot()).Count);
        await draft.ApplyAsync(_=>Task.CompletedTask);Assert.False(draft.IsDirty);
    }

    [Fact] public void ExistingSettingsLoadEmptyAndRoundTripRetainsUnrelatedSettingsAndLastPage()
    {
        string directory=Path.Combine(Path.GetTempPath(),"AuriHelm-tests-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"Priority\":[\"a\"],\"Automatic\":true}");
            var store=new SettingsStore(directory);var old=store.Load().Settings;Assert.Empty(old.QuickMenuExclusions);
            var next=old with{QuickMenuExclusions=new(){{AppExclusions.SystemSounds,"系統音效"}},LastPage="exclusions",Theme="dark",Hotkeys=new(){{"menu","Ctrl+Alt+F12"}}};
            store.Save(next);var loaded=store.Load().Settings;Assert.True(loaded.Automatic);Assert.Equal(old.Priority,loaded.Priority);Assert.Equal("exclusions",loaded.LastPage);Assert.Equal("dark",loaded.Theme);Assert.Equal("Ctrl+Alt+F12",loaded.Hotkeys["menu"]);Assert.Single(loaded.QuickMenuExclusions);
            Assert.Throws<InvalidDataException>(()=>store.Save(next with{QuickMenuExclusions=new(){{"session:1","unstable"}}}));
            Assert.Single(store.Load().Settings.QuickMenuExclusions);
            using(var locked=new FileStream(Path.Combine(directory,"settings.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Assert.ThrowsAny<IOException>(()=>store.Save(next with{QuickMenuExclusions=[]}));
            Assert.Single(store.Load().Settings.QuickMenuExclusions);Assert.Empty(Directory.GetFiles(directory,"*.tmp"));
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact] public void AppliedExclusionsRefreshQuickAndManagementPageButAudioValuesDoNot()
    {
        var tracker=new ViewRefreshTracker();var snapshot=Snapshot(Output("a",Session("one",Chrome)));var settings=new AppSettings();
        tracker.Observe(snapshot,settings,null,"exclusions",true);
        var changed=settings with{QuickMenuExclusions=new(){{AppExclusions.Key(Chrome)!,"chrome"}}};
        var plan=tracker.Observe(snapshot,changed,null,"exclusions",true);Assert.True(plan.Quick);Assert.True(plan.Full);Assert.True(plan.PreserveDraft);
        plan=tracker.Observe(snapshot with{ObservedAt=DateTimeOffset.UtcNow},changed,null,"exclusions",true);Assert.False(plan.Quick);Assert.False(plan.Full);
    }
}
