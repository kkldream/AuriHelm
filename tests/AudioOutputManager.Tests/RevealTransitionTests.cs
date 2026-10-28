using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class RevealTransitionTests
{
    [Fact]
    public void ReverseKeepsCurrentProgressAndFinishesLatestIntent()
    {
        var state=new RevealTransition(false);state.Set(true,0,true);
        double before=state.Progress(60);Assert.InRange(before,.01,.99);
        state.Set(false,60,true);Assert.Equal(before,state.Progress(60),5);
        Assert.True(state.Progress(90)<before);
        double closing=state.Progress(90);state.Set(true,90,true);Assert.Equal(closing,state.Progress(90),5);
        Assert.Equal(1,state.Progress(270));Assert.False(state.Running(270));Assert.True(state.Open);
    }
    [Fact]
    public void ReducedMotionImmediatelySettlesBothDirections()
    {
        var state=new RevealTransition(false);state.Set(true,0,false);Assert.Equal(1,state.Progress(0));Assert.False(state.Running(0));
        state.Set(false,1,false);Assert.Equal(0,state.Progress(1));Assert.False(state.Running(1));
    }
}
