using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class ReconnectionTrackerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=DateTimeOffset.Parse("2026-10-18T00:00:00Z");
        public override DateTimeOffset GetUtcNow()=>Now;
        public override long GetTimestamp()=>Now.UtcTicks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
    }
    private static AudioSnapshot Snapshot(bool headset,string current="speaker")=>new(
        headset?[new("speaker","Speaker",true,.2f,false,null,[]),new("headset","PRO X 2",true,.5f,false,null,[],IsProX2:true)]:[new("speaker","Speaker",true,.2f,false,null,[])],
        Enum.GetValues<AudioRole>().ToDictionary(r=>r,_=>(string?)current),DateTimeOffset.UtcNow);
    [Fact] public void TimeAloneOrAnUnlistedDeviceCannotSuppressExternalChoice()
    {
        var tracker=new ReconnectionTracker();tracker.Observe(Snapshot(true));
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        tracker.Observe(Snapshot(false));tracker.Observe(Snapshot(true));
        Assert.False(tracker.IsExpectedReturn("headset",["speaker"]));
        Assert.False(tracker.IsExpectedReturn("speaker",["headset","speaker"]));
    }
    [Fact] public void MissingCallbacksDoNotLeaveAnUnlimitedReturnExemption()
    {
        var clock=new Clock();var tracker=new ReconnectionTracker(clock);
        tracker.Observe(Snapshot(true));tracker.Observe(Snapshot(false));tracker.Observe(Snapshot(true));
        Assert.True(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        clock.Now+=TimeSpan.FromSeconds(6);
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
    }
    [Fact] public void StableRolesAndVerifiedOnEndReturnEpisodeBeforeTheDeadline()
    {
        var tracker=new ReconnectionTracker();tracker.Observe(Snapshot(true));tracker.Observe(Snapshot(false));tracker.Observe(Snapshot(true));
        tracker.Settle(Snapshot(true,"headset"),"headset",HeadsetState.Unknown,true);
        Assert.True(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        tracker.Settle(Snapshot(true,"headset"),"headset",HeadsetState.On,true);
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
    }
    [Fact] public void DelayedRemovalExemptsOnlyEarlierListedNotificationsWithinTheReturnDeadline()
    {
        var clock=new Clock();var tracker=new ReconnectionTracker(clock);
        tracker.Observe(Snapshot(true));tracker.Observe(Snapshot(false));
        long removalReceived=clock.GetTimestamp();
        clock.Now+=TimeSpan.FromSeconds(1);tracker.Observe(Snapshot(true));
        Assert.True(tracker.IsDelayedRemoval("headset",removalReceived,["headset","speaker"]));
        Assert.False(tracker.IsDelayedRemoval("headset",clock.GetTimestamp(),["headset","speaker"]));
        Assert.False(tracker.IsDelayedRemoval("headset",removalReceived,["speaker"]));
        Assert.False(tracker.IsDelayedRemoval("speaker",removalReceived,["headset","speaker"]));
        clock.Now+=TimeSpan.FromSeconds(6);
        Assert.False(tracker.IsDelayedRemoval("headset",removalReceived,["headset","speaker"]));
    }
    [Fact] public void OnlyReliablePowerCycleGrantsBoundedReturnWithoutEndpointRemoval()
    {
        var clock=new Clock();var tracker=new ReconnectionTracker(clock);tracker.Observe(Snapshot(true));
        tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        clock.Now+=TimeSpan.FromMilliseconds(500);
        tracker.ObserveHeadset("headset",new(HeadsetState.Off,clock.Now));
        clock.Now+=TimeSpan.FromMilliseconds(500);
        tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        Assert.True(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        Assert.False(tracker.IsExpectedReturn("headset",["speaker"]));
        Assert.False(tracker.IsDelayedRemoval("headset",clock.GetTimestamp()-1,["headset","speaker"]));
        clock.Now+=TimeSpan.FromSeconds(1);
        tracker.ObserveHeadset("headset",new(HeadsetState.Unknown,clock.Now));
        Assert.True(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        clock.Now+=TimeSpan.FromSeconds(5);
        tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
    }
    [Fact] public void UnknownAndStaleOrRepeatedSamplesCannotInventPowerReturn()
    {
        var clock=new Clock();var tracker=new ReconnectionTracker(clock);tracker.Observe(Snapshot(true));
        tracker.ObserveHeadset("headset",new(HeadsetState.Unknown,clock.Now));
        clock.Now+=TimeSpan.FromMilliseconds(500);
        tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        tracker.ObserveHeadset("headset",new(HeadsetState.Off,clock.Now-TimeSpan.FromSeconds(3)));
        tracker.ObserveHeadset("headset",new(HeadsetState.Off,clock.Now));
        clock.Now+=TimeSpan.FromMilliseconds(500);
        tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
    }
    [Fact] public void SettledPowerReturnAndExplicitResetKeepLaterManualChoicesEffective()
    {
        var clock=new Clock();var tracker=new ReconnectionTracker(clock);tracker.Observe(Snapshot(true));
        tracker.ObserveHeadset("headset",new(HeadsetState.Off,clock.Now));
        clock.Now+=TimeSpan.FromMilliseconds(500);tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        tracker.Settle(Snapshot(true,"headset"),"headset",HeadsetState.Unknown,true);
        Assert.True(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        tracker.Settle(Snapshot(true,"headset"),"headset",HeadsetState.On,true);
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
        clock.Now+=TimeSpan.FromMilliseconds(500);tracker.ObserveHeadset("headset",new(HeadsetState.Off,clock.Now));
        clock.Now+=TimeSpan.FromMilliseconds(500);tracker.ObserveHeadset("headset",new(HeadsetState.On,clock.Now));
        tracker.ClearPending();
        Assert.False(tracker.IsExpectedReturn("headset",["headset","speaker"]));
    }
}
