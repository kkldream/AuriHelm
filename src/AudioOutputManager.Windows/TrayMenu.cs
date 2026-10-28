namespace AudioOutputManager.Windows;

public enum TrayMenuCommand { None=0,OpenPanel=1,Exit=2 }
public sealed record TrayMenuItem(TrayMenuCommand Command,string Text,string Glyph);

/// <summary>The WinUI host and command tests share one ordered, localized command contract.</summary>
public static class TrayMenu
{
    public static IReadOnlyList<TrayMenuItem> Items(string openPanel,string exit)=>
    [new(TrayMenuCommand.OpenPanel,openPanel,"\uE8A0"),new(TrayMenuCommand.Exit,exit,"\uE7E8")];
}