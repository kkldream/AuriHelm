namespace AudioOutputManager.Core;

public static class CenturionPacket
{
    // MIT interoperability portions; see THIRD-PARTY-NOTICES.md.
    public static HeadsetState Classify(ReadOnlySpan<byte> packet)
    {
        if(packet.Length>=13 && packet[0]==0x51 && packet[1]==0x0B && packet[8]==0x04 && packet[10]<=100) return HeadsetState.On;
        ReadOnlySpan<byte> off=[0x51,0x05,0,0xFF,0x03,0x1A,0x0B,0];
        return packet.StartsWith(off)?HeadsetState.Off:HeadsetState.Unknown;
    }
}
