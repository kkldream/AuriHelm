using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class ViewRefreshTests
{
    private static AudioSnapshot Snapshot(params AudioOutput[] outputs)=>new(outputs,Enum.GetValues<AudioRole>().ToDictionary(r=>r,_=>(string?)"speaker"),DateTimeOffset.UtcNow);
    private static readonly AudioOutput Headset=new("headset","耳機",true,.5f,false,null,[]);
    private static readonly AudioOutput Speaker=new("speaker","喇叭",true,.2f,false,null,[]);
    [Fact] public void UnplugRefreshesPriorityPageWithoutDiscardingUnappliedEdits()
    {
        var tracker=new ViewRefreshTracker();var settings=new AppSettings{Priority=["headset","speaker"]};
        tracker.Observe(Snapshot(Headset,Speaker),settings,"headset","auto",true);
        var plan=tracker.Observe(Snapshot(Speaker),settings,null,"auto",true);
        Assert.True(plan.Full);Assert.True(plan.PreserveDraft);
    }
    [Fact] public void HidAssociationArrivingAfterEndpointAddsHeadsetStatusWithoutNavigation()
    {
        var tracker=new ViewRefreshTracker();var snapshot=Snapshot(Headset,Speaker);var settings=new AppSettings();
        tracker.Observe(snapshot,settings,null,"devices",false);
        var plan=tracker.Observe(snapshot,settings,"headset","devices",false);
        Assert.True(plan.Full);Assert.True(plan.Quick);
    }
    [Fact] public void AvailabilityChangeRefreshesEvenWhenEndpointIdRemainsPresent()
    {
        var tracker=new ViewRefreshTracker();var settings=new AppSettings();
        tracker.Observe(Snapshot(Headset,Speaker),settings,"headset","auto",false);
        Assert.True(tracker.Observe(Snapshot(Headset with{Available=false},Speaker),settings,"headset","auto",false).Full);
    }
    [Fact] public void VolumeUpdatesDoNotRebuildControlsOrMoveKeyboardFocus()
    {
        var tracker=new ViewRefreshTracker();var settings=new AppSettings();
        tracker.Observe(Snapshot(Headset,Speaker),settings,"headset","devices",false);
        var plan=tracker.Observe(Snapshot(Headset with{Volume=.7f,Muted=true},Speaker),settings,"headset","devices",false);
        Assert.False(plan.Full);Assert.False(plan.Quick);
    }
}
