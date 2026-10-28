using AudioOutputManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AudioOutputManager.App;

internal sealed partial class ShellWindow
{
    private AppExclusionDraft? exclusionDraft;
    private StackPanel? exclusionList;
    private TextBlock? exclusionCount, exclusionEmpty, exclusionError;
    private Button? exclusionApply, exclusionDiscard;
    private ContentControl? exclusionEditor;
    private bool syncingExclusions, savingExclusions;
    private readonly Dictionary<string,ExclusionRow> exclusionRows=[];
    private sealed record ExclusionRow(Border View,TextBlock Name,TextBlock State,CheckBox Check,AppIconPresenter Icon);

    private void ExclusionsPage()
    {
        var draft=exclusionDraft!;
        var editor=new Grid{Padding=pageHost.Padding,RowSpacing=12};
        exclusionEditor=new ContentControl{Content=editor,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        editor.RowDefinitions.Add(new(){Height=GridLength.Auto});editor.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var heading=new Grid{ColumnSpacing=12};heading.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        heading.Children.Add(Text(T("應用程式排除","App exclusions"),24));
        exclusionCount=Text("",12,true);Grid.SetColumn(exclusionCount,1);heading.Children.Add(exclusionCount);
        var search=new TextBox{Text=draft.Search,PlaceholderText=T("搜尋應用程式","Search apps"),Background=theme.Card,Foreground=theme.Text,BorderBrush=theme.Line};
        AutomationProperties.SetName(search,T("搜尋應用程式","Search apps"));
        search.TextChanged+=(_,_)=>{draft.Search=search.Text;RefreshExclusionRows();};
        var all=Button(T("全部","All"),()=>{draft.OnlyExcluded=false;RefreshExclusionRows();return Task.CompletedTask;});
        var excluded=Button(T("已排除","Excluded"),()=>{draft.OnlyExcluded=true;RefreshExclusionRows();return Task.CompletedTask;});
        void StyleFilters(){all.Background=draft.OnlyExcluded?theme.Card:theme.Hover;all.Foreground=draft.OnlyExcluded?theme.Text:theme.Blue;excluded.Background=draft.OnlyExcluded?theme.Hover:theme.Card;excluded.Foreground=draft.OnlyExcluded?theme.Blue:theme.Text;}
        all.Click+=(_,_)=>StyleFilters();excluded.Click+=(_,_)=>StyleFilters();StyleFilters();
        var columns=new Grid{Padding=new(14,0,18,0)};columns.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=GridLength.Auto});columns.Children.Add(Text(T("應用程式","Application"),12,true));var excludeLabel=Text(T("排除","Exclude"),12,true);Grid.SetColumn(excludeLabel,1);columns.Children.Add(excludeLabel);
        var tools=new StackPanel{Spacing=12};tools.Children.Add(heading);tools.Children.Add(Text(T("勾選後只在快速選單隱藏，仍可在完整面板調整音量。","Hide checked apps only in the quick menu; full-panel volume controls remain available."),14,true));tools.Children.Add(search);tools.Children.Add(Actions(all,excluded));tools.Children.Add(columns);editor.Children.Add(tools);
        exclusionList=new StackPanel();
        exclusionEmpty=Text(T("沒有符合的應用程式","No matching apps"),14,true);exclusionEmpty.Margin=new(14);
        var listContent=new StackPanel();listContent.Children.Add(exclusionList);listContent.Children.Add(exclusionEmpty);
        var listScroll=new ScrollViewer{Content=listContent,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var card=Card(listScroll);Grid.SetRow(card,1);editor.Children.Add(card);
        pageScroll!.Content=exclusionEditor;pageScroll.VerticalScrollMode=ScrollMode.Disabled;pageScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
        editor.SizeChanged+=(_,_)=>{bool narrow=editor.ActualWidth<510;editor.Padding=new(narrow?16:24);};
        exclusionError=Text("",12,true);exclusionError.Foreground=theme.Error;exclusionError.Visibility=Visibility.Collapsed;
        exclusionDiscard=Button(T("捨棄變更","Discard"),()=>
        {
            exclusionDraft=new(controller.Settings.QuickMenuExclusions){Search=draft.Search,OnlyExcluded=draft.OnlyExcluded};
            dirty=false;RenderPage(true);return Task.CompletedTask;
        });
        exclusionApply=Button(T("套用","Apply"),ApplyExclusions);exclusionApply.Foreground=theme.Blue;exclusionApply.Background=theme.Hover;
        var buttons=Actions(exclusionDiscard,exclusionApply);buttons.HorizontalAlignment=HorizontalAlignment.Right;
        applyDock.Child=Stack(exclusionError,buttons);applyDock.Visibility=Visibility.Visible;
        applyPending=ApplyExclusions;
        RefreshExclusionRows();
    }

    private async Task ApplyExclusions()
    {
        if(exclusionDraft is null||savingExclusions)return;
        savingExclusions=true;exclusionEditor!.IsEnabled=false;UpdateExclusionButtons();
        try
        {
            await exclusionDraft.ApplyAsync(next=>controller.SaveAsync(s=>s with{QuickMenuExclusions=next}));
            dirty=exclusionDraft.IsDirty;uiIssue=null;exclusionError!.Visibility=Visibility.Collapsed;
            RenderQuick();RefreshExclusionRows();
        }
        catch
        {
            exclusionError!.Text=T("無法儲存變更，請重試。你的勾選已保留，快速選單仍使用原設定。","Unable to save. Your selections are retained; the quick menu still uses the saved settings.");
            exclusionError.Visibility=Visibility.Visible;throw;
        }
        finally{savingExclusions=false;exclusionEditor!.IsEnabled=true;UpdateExclusionButtons();UpdateStatus();}
    }

    private void UpdateExclusionButtons()
    {
        if(exclusionDraft is null)return;
        if(exclusionApply is not null)exclusionApply.IsEnabled=exclusionDraft.IsDirty&&!savingExclusions;
        if(exclusionDiscard is not null)exclusionDiscard.IsEnabled=exclusionDraft.IsDirty&&!savingExclusions;
    }

    // Reconcile by identity instead of rebuilding the page: search, focus, draft and scroll survive notifications.
    private void RefreshExclusionRows()
    {
        if(exclusionDraft is null||exclusionList is null)return;
        var draft=exclusionDraft;var candidates=draft.Candidates(controller.Snapshot);
        var focused=fullRoot.XamlRoot is {} root?Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root) as Control:null;
        var focusState=focused?.FocusState??FocusState.Programmatic;
        var visible=draft.Filter(candidates).Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
        var ids=candidates.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
        foreach(string id in exclusionRows.Keys.Where(id=>!ids.Contains(id)).ToArray()){exclusionList.Children.Remove(exclusionRows[id].View);exclusionRows.Remove(id);}
        var duplicates=candidates.GroupBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        syncingExclusions=true;
        try
        {
            for(int i=0;i<candidates.Count;i++)
            {
                var app=candidates[i];
                if(!exclusionRows.TryGetValue(app.Id,out var row))
                {
                    var name=Text(app.Name);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;
                    var state=Text("",12,true);var labels=Stack(name,state);
                    var identity=SessionHeading(app.Sessions??[new(app.Id,app.Name,app.Id,Guid.Empty,0,false,null)],labels);
                    var check=new CheckBox{MinWidth=24,MinHeight=24,Padding=new(0),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,Foreground=theme.Text,FocusVisualPrimaryBrush=theme.Blue,FocusVisualSecondaryBrush=theme.Head};
                    if(!new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
                        foreach(string suffix in new[]{"","PointerOver","Pressed"})
                        {
                            check.Resources["CheckBoxCheckBackgroundFillChecked"+suffix]=theme.Blue;check.Resources["CheckBoxCheckBackgroundStrokeChecked"+suffix]=theme.Blue;
                            check.Resources["CheckBoxCheckBackgroundStrokeUnchecked"+suffix]=theme.Line;check.Resources["CheckBoxCheckBackgroundFillUnchecked"+suffix]=theme.Panel;
                        }
                    var grid=new Grid{ColumnSpacing=12,Padding=new(14,12,14,12)};grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new(24)});grid.Children.Add(identity);Grid.SetColumn(check,1);grid.Children.Add(check);
                    row=new(new Border{Child=grid,BorderBrush=theme.Line,BorderThickness=new(0,1,0,0)},name,state,check,(AppIconPresenter)identity.Children[0]);
                    exclusionRows.Add(app.Id,row);exclusionList.Children.Insert(i,row.View);
                    void Changed(object sender,RoutedEventArgs args)
                    {
                        if(syncingExclusions)return;
                        draft.Set(app,check.IsChecked==true);dirty=draft.IsDirty;exclusionError!.Visibility=Visibility.Collapsed;RefreshExclusionRows();UpdateStatus();
                    }
                    check.Checked+=Changed;check.Unchecked+=Changed;
                }
                int index=exclusionList.Children.IndexOf(row.View);
                if(index!=i){exclusionList.Children.RemoveAt(index);exclusionList.Children.Insert(i,row.View);}
                row.Name.Text=app.Name;
                row.Icon.Update(IconSessions(app.Sessions??[new(app.Id,app.Name,app.Id,Guid.Empty,0,false,null)]));
                row.State.Text=app.Key is null?T("暫不支援排除：無法可靠識別應用程式","Exclusion unavailable: app identity could not be verified"):
                    !app.Detected?(app.Saved?T("未偵測到 · 已儲存的排除記錄","Not detected · saved exclusion"):T("未偵測到 · 尚未套用","Not detected · unapplied selection")):
                    app.Groups>1?T("目前偵測到 · 涵蓋多個音訊群組","Detected · covers multiple audio groups"):T("目前偵測到","Detected");
                if(duplicates.Contains(app.Name))row.State.Text+=T(" · 同名應用程式，詳細識別見提示"," · Same name; identity in tooltip");
                ToolTipService.SetToolTip(row.View,app.Name+(duplicates.Contains(app.Name)?"\n"+(app.Key??T("暫時音訊工作階段","Temporary audio session")):""));
                row.Check.IsEnabled=app.Key is not null;row.Check.IsChecked=draft.IsExcluded(app.Key);
                AutomationProperties.SetName(row.Check,T("排除 ","Exclude ")+app.Name);
                AutomationProperties.SetHelpText(row.Check,row.State.Text+(duplicates.Contains(app.Name)?" · "+app.Key:""));
                row.View.Visibility=visible.Contains(app.Id)?Visibility.Visible:Visibility.Collapsed;
            }
        }
        finally{syncingExclusions=false;}
        if(focused is not null&&focusState!=FocusState.Unfocused&&focused.IsLoaded&&focused.Visibility==Visibility.Visible&&!ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(fullRoot.XamlRoot),focused))
            focused.Focus(focusState);
        exclusionEmpty!.Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
        exclusionCount!.Text=T($"已選擇排除 {draft.Count} 個",$"{draft.Count} apps excluded");UpdateExclusionButtons();
    }
}
