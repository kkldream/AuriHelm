using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class TrayInteractionTests
{
    [Fact]
    public void RightReleaseOpensCommandsWithoutTogglingQuickMenu()
    {
        var calls=new List<string>();var tray=new TrayInteraction(()=>calls.Add("quick"),()=>calls.Add("commands"));
        Assert.True(tray.Dispatch(0x205));Assert.Equal(new[]{"commands"},calls);
    }
    [Fact]
    public void LeftReleaseKeepsPrimaryQuickMenuAction()
    {
        var calls=new List<string>();var tray=new TrayInteraction(()=>calls.Add("quick"),()=>calls.Add("commands"));
        Assert.True(tray.Dispatch(0x202));Assert.Equal(new[]{"quick"},calls);
    }
    [Fact]
    public void ContextKeyboardAndPackedNotificationDoNotDoubleDispatchButtonDown()
    {
        int quick=0,commands=0;var tray=new TrayInteraction(()=>quick++,()=>commands++);
        Assert.False(tray.Dispatch(0x204));Assert.False(tray.Dispatch(0x200));
        Assert.True(tray.Dispatch(0x7b));Assert.True(tray.Dispatch(0x1000205));
        Assert.Equal(0,quick);Assert.Equal(2,commands);
    }
}
