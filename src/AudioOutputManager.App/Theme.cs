using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AudioOutputManager.App;

internal sealed class Theme(bool dark)
{
    public SolidColorBrush Panel {get;}=Brush(dark?0x202c41u:0xf7f9fcu,true);
    public SolidColorBrush Head {get;}=Brush(dark?0x193a60u:0xe4effcu,true);
    public SolidColorBrush Card {get;}=Brush(dark?0x253b53u:0xffffffu,true);
    public SolidColorBrush Hover {get;}=Brush(dark?0x314c69u:0xe2efffu,true);
    public SolidColorBrush Text {get;}=Brush(dark?0xeff6ffu:0x202b36u);
    public SolidColorBrush Muted {get;}=Brush(dark?0xb5c6dau:0x52657au);
    public SolidColorBrush Line {get;}=Brush(dark?0x425570u:0xc2cfddu);
    public SolidColorBrush Blue {get;}=Brush(dark?0x26a5edu:0x0067b8u);
    public SolidColorBrush Error {get;}=Brush(dark?0xffb5b5u:0xa32121u);
    public SolidColorBrush Peak {get;}=Brush(dark?0xffffffu:0x16283du);
    private static SolidColorBrush Brush(uint rgb,bool surface=false)
    {
        if(new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            return new(new global::Windows.UI.ViewManagement.UISettings().GetColorValue(surface?global::Windows.UI.ViewManagement.UIColorType.Background:global::Windows.UI.ViewManagement.UIColorType.Foreground));
        return new(Color.FromArgb(255,(byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));
    }
    public static ElementTheme Requested(string name)=>name switch {"light"=>ElementTheme.Light,"dark"=>ElementTheme.Dark,_=>ElementTheme.Default};
}
