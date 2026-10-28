using AudioOutputManager.Core;
using Xunit;

namespace AudioOutputManager.Tests;
public sealed class CenturionTests
{
    [Fact] public void InvalidAndShortFramesAreUnknown()
    {
        Assert.Equal(HeadsetState.Unknown,CenturionPacket.Classify([]));
        Assert.Equal(HeadsetState.Unknown,CenturionPacket.Classify([0x51,0x0B]));
        byte[] packet=new byte[13]; packet[0]=0x51;packet[1]=0x0B;packet[8]=4;packet[10]=101;
        Assert.Equal(HeadsetState.Unknown,CenturionPacket.Classify(packet));
        packet[10]=72;Assert.Equal(HeadsetState.On,CenturionPacket.Classify(packet));
        Assert.Equal(HeadsetState.Off,CenturionPacket.Classify([0x51,5,0,0xFF,3,0x1A,0x0B,0]));
    }
}
