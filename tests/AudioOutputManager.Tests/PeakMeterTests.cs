using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class PeakMeterTests
{
    [Fact]
    public void VolumeNotificationsDoNotBlankTheActivityBetweenMeterSamples()
    {
        var meter=new PeakMeterState();var start=DateTimeOffset.UtcNow;
        for(int i=0;i<60;i++)
        {
            meter.Observe(.6f,start.AddMilliseconds(i*50));
            meter.Observe(null,start.AddMilliseconds(i*50+10));
            Assert.Equal(.6f,meter.Value);
        }
    }

    [Fact]
    public void AnUnmeasuredNotificationDoesNotSupersedeAnActualMeterReading()
    {
        var meter=new PeakMeterState();var start=DateTimeOffset.UtcNow;
        meter.Observe(.6f,start);
        meter.Observe(null,start.AddMilliseconds(50));
        meter.Observe(.2f,start.AddMilliseconds(40));
        Assert.Equal(.2f,meter.Value);
        meter.Observe(.8f,start.AddMilliseconds(20));
        Assert.Equal(.2f,meter.Value);
    }

    [Fact]
    public void AMeasuredZeroClearsActivityImmediatelyButMissingDataDoesNotInventSilence()
    {
        var meter=new PeakMeterState();var start=DateTimeOffset.UtcNow;
        Assert.Null(meter.Value);
        meter.Observe(.6f,start);
        meter.Observe(0,start.AddMilliseconds(50));
        Assert.Equal(0,meter.Value);
        meter.Observe(null,start.AddMilliseconds(60));
        Assert.Equal(0,meter.Value);
    }
}
