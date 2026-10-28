using AudioOutputManager.Windows;
using Xunit;

namespace AudioOutputManager.Tests;

public sealed class TrayMenuTests
{
    [Fact]
    public void MenuContractHasOnlyOpenPanelThenExitWithLocalizedLabelsAndIcons()
    {
        var menu=TrayMenu.Items("開啟面板","結束程式");
        Assert.Equal(new[]{TrayMenuCommand.OpenPanel,TrayMenuCommand.Exit},menu.Select(i=>i.Command));
        Assert.Equal(new[]{"開啟面板","結束程式"},menu.Select(i=>i.Text));
        Assert.All(menu,i=>Assert.False(string.IsNullOrWhiteSpace(i.Glyph)));
        Assert.Equal(2,menu.Select(i=>i.Glyph).Distinct().Count());
        Assert.Equal(new[]{"Open panel","Exit"},TrayMenu.Items("Open panel","Exit").Select(i=>i.Text));
    }
}