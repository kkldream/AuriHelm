using AudioOutputManager.Core;
using AudioOutputManager.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AudioOutputManager.App;

internal sealed partial class ShellWindow
{
    private readonly AppIconReader appIcons=new();
    private AudioSession[] IconSessions(IEnumerable<AudioSession> sessions)
    {
        var members=sessions.ToArray();string key=AppExclusions.Key(members[0].AppIdentity)??members[0].AppIdentity;
        var known=controller.Snapshot.Outputs.SelectMany(d=>d.Sessions)
            .Where(s=>string.Equals(AppExclusions.Key(s.AppIdentity)??s.AppIdentity,key,StringComparison.Ordinal)).ToArray();
        return known.Length>0?known:members;
    }
    private Grid SessionHeading(IEnumerable<AudioSession> sessions,FrameworkElement label)
    {
        var members=sessions.ToArray();var session=members[0];
        var header=new Grid{ColumnSpacing=8};header.ColumnDefinitions.Add(new(){Width=new(20)});header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var icon=new AppIconPresenter(appIcons,theme.Muted);icon.Update(IconSessions(members));header.Children.Add(icon);
        if(label is TextBlock text){text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;}
        ToolTipService.SetToolTip(label,session.Name);Grid.SetColumn(label,1);header.Children.Add(label);return header;
    }
}
