using AudioOutputManager.Core;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AudioOutputManager.App;

internal sealed partial class ShellWindow
{
    private readonly (string Id,string Zh,string En)[] pages=[("devices","裝置與音量","Devices & volume"),("exclusions","應用程式排除","App exclusions"),("auto","自動切換","Automatic switching"),("keys","快捷鍵","Hotkeys"),("general","外觀與一般","Appearance & general"),("about","診斷與關於","Diagnostics & about")];
    private void BuildFull()
    {
        // Shared controls must be detached from their old logical parents before theme rebuilds.
        if(pageScroll is not null)pageScroll.Content=null;
        previousAside?.Children.Clear();previousContent?.Children.Clear();
        fullRoot.Children.Clear();fullRoot.ColumnDefinitions.Clear();
        fullRoot.RowDefinitions.Clear();fullRoot.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        fullRoot.ColumnDefinitions.Add(new(){Width=new(190)});fullRoot.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        nav.Children.Clear();
        foreach(var page in pages)
        {
            var button=NavigationButton(T(page.Zh,page.En),async()=>{if(await ResolvePending()){currentPage=page.Id;RenderPage();pageScroll?.ChangeView(null,0,null,true);}});
            button.Tag=page.Id;button.MinHeight=42;button.BorderThickness=new(0);button.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            string glyph=page.Id switch{"devices"=>"\uE7F6","exclusions"=>"\uE71C","auto"=>"\uE8AB","keys"=>"\uE765","general"=>"\uE713",_=>"\uE946"};
            button.Content=Actions(Icon(glyph),Text(T(page.Zh,page.En)));
            button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;nav.Children.Add(button);
        }
        var aside=new Grid{Background=theme.Head,Padding=new(12,18,12,18)};
        previousAside=aside;
        aside.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});aside.RowDefinitions.Add(new(){Height=GridLength.Auto});
        aside.Children.Add(nav);var quit=FlatButton(T("結束程式","Exit"),QuitAsync);quit.Content=Actions(Icon("\uE7E8"),Text(T("結束程式","Exit")));quit.HorizontalAlignment=HorizontalAlignment.Stretch;quit.HorizontalContentAlignment=HorizontalAlignment.Left;
        Grid.SetRow(quit,1);aside.Children.Add(quit);fullRoot.Children.Add(aside);
        var content=new Grid();content.RowDefinitions.Add(new(){Height=GridLength.Auto});content.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});content.RowDefinitions.Add(new(){Height=GridLength.Auto});
        previousContent=content;
        pageHost.Padding=new(24);
        var scroll=new ScrollViewer{Content=pageHost,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);content.Children.Add(scroll);
        pageScroll=scroll;
        applyDock.Background=theme.Panel;Grid.SetRow(applyDock,2);content.Children.Add(applyDock);Grid.SetColumn(content,1);fullRoot.Children.Add(content);
        void Arrange()
        {
            bool narrow=fullRoot.ActualWidth>0&&fullRoot.ActualWidth<650;
            pageHost.Padding=new(narrow?16:24);
            if(pageHost.Children.FirstOrDefault() is TextBlock heading)heading.FontSize=narrow?22:24;
            int columns=narrow?Math.Clamp((int)(fullRoot.ActualWidth/160),1,3):1;
            nav.ColumnDefinitions.Clear();nav.RowDefinitions.Clear();
            for(int i=0;i<columns;i++)nav.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            for(int i=0;i<(pages.Length+columns-1)/columns;i++)nav.RowDefinitions.Add(new(){Height=GridLength.Auto});
            for(int i=0;i<nav.Children.Count;i++){Grid.SetRow((FrameworkElement)nav.Children[i],i/columns);Grid.SetColumn((FrameworkElement)nav.Children[i],i%columns);}
            fullRoot.ColumnDefinitions[0].Width=narrow?new(1,GridUnitType.Star):new(190);fullRoot.ColumnDefinitions[1].Width=narrow?new(0):new(1,GridUnitType.Star);
            if(narrow&&fullRoot.RowDefinitions.Count==1){fullRoot.RowDefinitions[0].Height=GridLength.Auto;fullRoot.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});}
            if(!narrow&&fullRoot.RowDefinitions.Count>1){fullRoot.RowDefinitions.RemoveAt(1);fullRoot.RowDefinitions[0].Height=new(1,GridUnitType.Star);}
            Grid.SetRow(content,narrow?1:0);Grid.SetColumn(content,narrow?0:1);
        }
        if(layoutChanged is not null)fullRoot.SizeChanged-=layoutChanged;
        layoutChanged=(_,_)=>Arrange();fullRoot.SizeChanged+=layoutChanged;Arrange();
        RenderPage(dirty);
    }
    private void RenderPage(bool preserveDraft=false)
    {
        if(pageScroll is not null){pageScroll.Content=pageHost;pageScroll.VerticalScrollMode=ScrollMode.Enabled;pageScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;}
        exclusionRows.Clear();exclusionList=null;
        foreach(var key in audioRows.Keys.Where(k=>!k.StartsWith("quick")).ToArray())audioRows.Remove(key);
        meterRows.RemoveAll(r=>!r.Key.StartsWith("quick"));
        pageHost.Children.Clear();applyPending=null;if(!preserveDraft)pendingRemovals.Clear();applyDock.Child=null;applyDock.Visibility=Visibility.Collapsed;
        UpdateStatus();status.Foreground=theme.Muted;
        if(!pages.Any(p=>p.Id==currentPage))currentPage="devices";
        var page=pages.First(p=>p.Id==currentPage);pageHost.Children.Add(Text(T(page.Zh,page.En),fullRoot.ActualWidth>0&&fullRoot.ActualWidth<650?22:24));
        string description=currentPage switch
        {
            "devices"=>T("目前輸出與各應用程式音量","Current output and application volumes"),
            "exclusions"=>T("勾選後只在快速選單隱藏，仍可在完整面板調整音量。","Hide checked apps only in the quick menu; full-panel volume controls remain available."),
            "auto"=>T("只在你加入的裝置中，依優先序選擇可用輸出","Choose available outputs in your configured priority order"),
            "keys"=>T("預設全部不綁定；由你決定全域快捷鍵","All shortcuts are unbound by default"),
            "general"=>T("語言、主題與常駐行為","Language, theme and background behavior"),
            _=>T("本機版本與診斷資訊","Local version and diagnostics")
        };
        var subtitle=Text(description,14,true);subtitle.Margin=new(0,-12,0,2);pageHost.Children.Add(subtitle);
        foreach(var button in nav.Children.OfType<Button>())
        {
            bool selected=(string)button.Tag==currentPage;button.Background=new SolidColorBrush(selected?theme.Hover.Color:Microsoft.UI.Colors.Transparent);
            if(button.Content is StackPanel parts)foreach(var part in parts.Children){if(part is TextBlock text)text.Foreground=selected?theme.Blue:theme.Text;if(part is FontIcon icon)icon.Foreground=selected?theme.Blue:theme.Muted;}
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button,selected?T("目前分類","Current category"):"");
        }
        switch(currentPage)
        {
            case "devices": DevicesPage();break;
            case "exclusions":if(!preserveDraft||exclusionDraft is null)exclusionDraft=new(controller.Settings.QuickMenuExclusions);ExclusionsPage();break;
            case "auto":if(!preserveDraft){draftPriority=controller.Settings.Priority.ToList();draftOff=controller.Settings.OffConfirmationSeconds;}AutoPage();break;
            case "keys":if(!preserveDraft)draftHotkeys=new(controller.Settings.Hotkeys);KeysPage();break;
            case "general":GeneralPage();break;
            case "about":AboutPage();break;
        }
        UpdateValues(controller.Snapshot);
        UpdateMeterSchedule();
    }
    private void DevicesPage()
    {
        foreach(var output in controller.Snapshot.Outputs.OrderByDescending(d=>d.Id==controller.Snapshot.CurrentId).ThenBy(Name))
        {
            bool current=output.Id==controller.Snapshot.CurrentId;
            var name=Text(Name(output),16);name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(name,Name(output));
            var edit=FlatButton(T("編輯別名：","Edit alias: ")+Name(output),()=>EditAlias(output));
            edit.Content=Icon("\uE70F",14);edit.Width=edit.Height=28;edit.MinWidth=edit.MinHeight=28;edit.Padding=new(4);
            ToolTipService.SetToolTip(edit,T("編輯別名","Edit alias"));
            var labels=Stack(Actions(name,edit));
            labels.SizeChanged+=(_,_)=>name.MaxWidth=Math.Max(32,labels.ActualWidth-36);
            var state=Text(DeviceState(output),12,true);labels.Children.Add(state);
            audioRows["full-device-state"+output.Id]=_=>state.Text=DeviceState(output);
            if(Name(output)!=output.Name)labels.Children.Add(Text(output.Name,12,true));
            if(output.Id==controller.HeadsetId)
            {
                audioRows["full-headset"+output.Id]=_=>state.Text=HeadsetText()+" · "+BatteryText()+T(" · 更新 "," · Updated ")+controller.Headset.ObservedAt.ToLocalTime().ToString("HH:mm:ss");
            }
            var heading=new Grid{ColumnSpacing=14};heading.ColumnDefinitions.Add(new(){Width=new(20)});heading.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});heading.Children.Add(Icon(DeviceGlyph(output)));Grid.SetColumn(labels,1);heading.Children.Add(labels);
            UIElement action=current?Tag(T("目前輸出","Current output")):Button(T("切換至此裝置","Switch to this output"),()=>controller.ManualSwitchAsync(output.Id));
            var volume=Rows(Inset(Stack(Text(T("輸出總音量","Output volume"),12,true),EndpointRow(output,"full-total"+output.Id)),true),Mixer(output,"full",true));
            pageHost.Children.Add(Card(Rows(Row(heading,action),Disclosure("device:"+output.Id,T(current?"音量與應用程式":"查看音量",current?"Volume and applications":"View volume"),volume,current))));
        }
        if(controller.Snapshot.Outputs.Count==0)pageHost.Children.Add(Card(Text(T("沒有可用輸出","No output available"))));
        if(!controller.Snapshot.RolesConsistent)
        {
            var roles=Stack(Text(T("輸出未一致","Output roles differ"),16));
            foreach(var role in controller.Snapshot.Defaults)roles.Children.Add(Text(role.Key+": "+(role.Value is null?T("未知","Unknown"):Name(role.Value))));
            pageHost.Children.Add(Card(roles));
        }
        var offline=controller.Settings.Priority.Concat(controller.Settings.KnownDevices.Keys).Distinct().Where(id=>!controller.Snapshot.Outputs.Any(d=>d.Id==id)).ToArray();
        var offlineItems=new List<UIElement>();
        foreach(string id in offline)
        {
            var remove=Button(T("移除記錄","Remove record"),()=>
            {
                dirty=true;
                pendingRemovals.Add(id);
                applyPending=()=>controller.SaveAsync(s=>s with{Priority=s.Priority.Where(x=>!pendingRemovals.Contains(x)).ToArray(),Aliases=s.Aliases.Where(x=>!pendingRemovals.Contains(x.Key)).ToDictionary(),KnownDevices=s.KnownDevices.Where(x=>!pendingRemovals.Contains(x.Key)).ToDictionary()});
                UpdateStatus();
                ShowApplyBar();return Task.CompletedTask;
            });
            offlineItems.Add(Row(Stack(Text(Name(id)),Text(T("離線 · 保留排序與別名","Offline · priority and alias retained"),12,true)),remove));
        }
        if(offline.Length>0)pageHost.Children.Add(Disclosure("offline",T($"離線裝置（{offline.Length}）",$"Offline devices ({offline.Length})"),Card(Rows(offlineItems.ToArray()))));
        if(pendingRemovals.Count>0)
        {
            applyPending=()=>controller.SaveAsync(s=>s with{Priority=s.Priority.Where(x=>!pendingRemovals.Contains(x)).ToArray(),Aliases=s.Aliases.Where(x=>!pendingRemovals.Contains(x.Key)).ToDictionary(),KnownDevices=s.KnownDevices.Where(x=>!pendingRemovals.Contains(x.Key)).ToDictionary()});ShowApplyBar();
        }
    }
    private async Task EditAlias(AudioOutput device)
    {
        var input=new TextBox{Text=controller.Settings.Aliases.GetValueOrDefault(device.Id,""),PlaceholderText=device.Name,MaxLength=100,Header=T("只影響本工具顯示","Only changes this app's display name")};
        var dialog=new ContentDialog{Title=T("編輯裝置別名","Edit device alias"),Content=input,PrimaryButtonText=T("儲存","Save"),CloseButtonText=T("取消","Cancel"),XamlRoot=fullRoot.XamlRoot,RequestedTheme=fullRoot.RequestedTheme};
        StyleDialog(dialog);
        if(await dialog.ShowAsync()==ContentDialogResult.Primary)
        {
            await controller.SaveAsync(s=>{var aliases=new Dictionary<string,string>(s.Aliases);if(string.IsNullOrWhiteSpace(input.Text))aliases.Remove(device.Id);else aliases[device.Id]=input.Text.Trim();return s with{Aliases=aliases};});RenderAll();
        }
    }
    private StackPanel ApplyBar()
    {
        var bar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        bar.Children.Add(Button(T("捨棄變更","Discard"),()=>{dirty=false;RenderPage();return Task.CompletedTask;}));
        var apply=Button(T("套用","Apply"),async()=>{if(applyPending is not null)await applyPending();dirty=false;RenderPage();});apply.Background=theme.Hover;apply.Foreground=theme.Blue;bar.Children.Add(apply);return bar;
    }
    private void ShowApplyBar(){applyDock.Child=ApplyBar();applyDock.Visibility=Visibility.Visible;}
    private void AutoPage()
    {
        while(pageHost.Children.Count>2)pageHost.Children.RemoveAt(pageHost.Children.Count-1);
        var toggle=new ToggleSwitch{IsOn=controller.Settings.Automatic,OnContent=T("開啟","On"),OffContent=T("關閉","Off"),MinWidth=80};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,T("自動切換","Automatic switching"));
        var autoStatus=Text(StatusText(),12,true);bool syncing=false;
        toggle.Toggled+=async(_,_)=>{if(syncing)return;try{await controller.SetAutomaticAsync(toggle.IsOn);}catch(Exception ex){Error(ex);syncing=true;toggle.IsOn=controller.Settings.Automatic;syncing=false;}};
        audioRows["full-auto-toggle"]=_=>{syncing=true;toggle.IsOn=controller.Settings.Automatic;syncing=false;autoStatus.Text=StatusText();};
        pageHost.Children.Add(Card(Row(Stack(Text(T("自動切換","Automatic switching"),16),autoStatus),toggle)));
        pageHost.Children.Add(Text(T("裝置優先序","Device priority"),16));
        var priorityItems=new System.Collections.ObjectModel.ObservableCollection<UIElement>();
        for(int i=0;i<draftPriority.Count;i++)
        {
            int index=i;string id=draftPriority[i];bool online=controller.Snapshot.Outputs.Any(d=>d.Id==id&&d.Available);
            var label=Stack(Text(Name(id)),Text(online?T("可用","Available")+(id==controller.Snapshot.CurrentId?T(" · 目前輸出"," · Current output"):""):T("離線 · 保留排序與別名","Offline · priority and alias retained"),12,true));
            var up=Button("↑",()=>{if(index>0){(draftPriority[index-1],draftPriority[index])=(draftPriority[index],draftPriority[index-1]);dirty=true;AutoPage();}return Task.CompletedTask;});up.IsEnabled=i>0;
            var down=Button("↓",()=>{if(index<draftPriority.Count-1){(draftPriority[index+1],draftPriority[index])=(draftPriority[index],draftPriority[index+1]);dirty=true;AutoPage();}return Task.CompletedTask;});down.IsEnabled=i<draftPriority.Count-1;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up,Name(id)+T(" 上移"," Move up"));Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down,Name(id)+T(" 下移"," Move down"));
            var identity=new Grid{ColumnSpacing=14};identity.ColumnDefinitions.Add(new(){Width=new(20)});identity.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});identity.Children.Add(Text((i+1).ToString(),14,true));Grid.SetColumn(label,1);identity.Children.Add(label);
            var item=new Border{Child=Row(identity,Actions(up,down,Button(T("移出","Remove"),()=>{draftPriority.Remove(id);dirty=true;AutoPage();return Task.CompletedTask;}))),BorderBrush=theme.Line,BorderThickness=new(0,i==0?0:1,0,0),Tag=id};priorityItems.Add(item);
        }
        var priorityList=new ListView{ItemsSource=priorityItems,CanDragItems=true,CanReorderItems=true,AllowDrop=true,ReorderMode=ListViewReorderMode.Enabled,SelectionMode=ListViewSelectionMode.None,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(0)};
        var itemStyle=new Style(typeof(ListViewItem));itemStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.MarginProperty,new Thickness(0)));itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));priorityList.ItemContainerStyle=itemStyle;
        priorityList.DragItemsCompleted+=(_,args)=>{if(args.DropResult==global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None)return;draftPriority=priorityItems.OfType<FrameworkElement>().Select(e=>(string)e.Tag).ToList();dirty=true;AutoPage();};
        pageHost.Children.Add(Card(draftPriority.Count>0?priorityList:Inset(Text(T("尚未設定優先序；請先加入裝置。","No priority configured. Add an output below."),14,true))));
        var candidates=controller.Snapshot.Outputs.Where(d=>!draftPriority.Contains(d.Id)).ToArray();
        if(candidates.Length>0)
        {
            pageHost.Children.Add(Text(T("尚未加入","Not in priority"),16));
            pageHost.Children.Add(Card(Rows(candidates.Select(device=>(UIElement)Row(Stack(Text(Name(device)),Text(T("可用 · 不參與自動切換","Available · excluded from automatic switching"),12,true)),Button(T("加入優先序","Add to priority"),()=>{draftPriority.Add(device.Id);dirty=true;AutoPage();return Task.CompletedTask;}))).ToArray())));
        }
        var off=new ComboBox{ItemsSource=new[]{1,2,3,4,5},SelectedItem=draftOff,MinWidth=100};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(off,T("耳機關閉確認時間，秒","Headset off confirmation, seconds"));
        off.SelectionChanged+=(_,_)=>{if(off.SelectedItem is int seconds){draftOff=seconds;dirty=true;}};
        pageHost.Children.Add(Card(Row(Stack(Text(T("耳機關閉確認時間","Headset off confirmation")),Text(T("單位：秒。Unknown 不當關閉，也不累計確認時間。","Seconds. Unknown readings never count as off confirmation."),12,true)),off)));
        pageHost.Children.Add(Text(T("手動選擇輸出會關閉自動；排序修改按套用後生效。","Manual output selection disables automatic switching. Apply to save priority edits."),12,true));
        applyPending=()=>controller.SaveAsync(s=>s with{Priority=draftPriority.ToArray(),OffConfirmationSeconds=draftOff,KnownDevices=s.KnownDevices.Concat(controller.Snapshot.Outputs.Select(d=>new KeyValuePair<string,string>(d.Id,d.Name))).GroupBy(x=>x.Key).ToDictionary(g=>g.Key,g=>g.Last().Value)});ShowApplyBar();
    }
    private void KeysPage()
    {
        var rows=new List<UIElement>();
        foreach(var (id,zh,en,descriptionZh,descriptionEn) in new[]{("menu","開啟／收起快速選單","Show / hide quick menu","從任何應用程式快速調整音量","Adjust volume from any application"),("next","下一個可用輸出","Next available output","依已設定優先序循環，並關閉自動切換","Cycle through priority outputs and turn automatic off"),("automatic","開啟／關閉自動切換","Toggle automatic switching","切換唯一的自動開關","Toggle the automatic switching setting")})
        {
            var binding=Text(draftHotkeys.GetValueOrDefault(id,"") is {Length:>0} key?key:T("未設定","Unbound"),12,true);
            var badge=new Border{Child=binding,Background=theme.Panel,BorderBrush=theme.Line,BorderThickness=new(1),Padding=new(10,5,10,5),CornerRadius=new(5),VerticalAlignment=VerticalAlignment.Center};
            var set=Button(T("設定按鍵","Set shortcut"),async()=>
            {
                var result=await CaptureShortcut(T(zh,en),draftHotkeys.GetValueOrDefault(id,""));if(result is null)return;draftHotkeys[id]=result;binding.Text=result.Length>0?result:T("未設定","Unbound");dirty=true;
            });
            var clear=FlatButton(T("清除","Clear"),()=>{draftHotkeys[id]="";binding.Text=T("未設定","Unbound");dirty=true;return Task.CompletedTask;});
            rows.Add(Row(Stack(Text(T(zh,en)),Text(T(descriptionZh,descriptionEn),12,true)),Actions(badge,set,clear)));
        }
        pageHost.Children.Add(Card(Rows(rows.ToArray())));
        pageHost.Children.Add(Text(T("快捷鍵套用時檢查衝突；取消或捨棄不會變更既有綁定。","Conflicts are checked on Apply. Cancel or Discard retains existing bindings."),12,true));
        applyPending=async()=>{using var plan=hotkeys.Prepare(draftHotkeys);await controller.SaveAsync(s=>s with{Hotkeys=new(draftHotkeys)});plan.Commit();};ShowApplyBar();
    }
    private async Task<string?> CaptureShortcut(string label,string initial)
    {
        var input=new TextBox{Text=initial,IsReadOnly=true,PlaceholderText=T("請按組合鍵，例如 Ctrl + Alt + F12","Press a shortcut, e.g. Ctrl + Alt + F12"),Header=label,MinWidth=300};
        input.KeyDown+=(_,args)=>
        {
            int key=(int)args.Key;if(key is 16 or 17 or 18 or 91 or 92)return;
            if(key is 8 or 46){input.Text="";args.Handled=true;return;}if(key==27)return;
            var modifiers=new List<string>();
            if((NativeShell.GetKeyState(17)&0x8000)!=0)modifiers.Add("Ctrl");if((NativeShell.GetKeyState(18)&0x8000)!=0)modifiers.Add("Alt");if((NativeShell.GetKeyState(16)&0x8000)!=0)modifiers.Add("Shift");if((NativeShell.GetKeyState(91)&0x8000)!=0||(NativeShell.GetKeyState(92)&0x8000)!=0)modifiers.Add("Win");
            if(!modifiers.Any(m=>m is "Ctrl" or "Alt" or "Win"))return;
            modifiers.Add(key is >=48 and <=57 or >=65 and <=90?((char)key).ToString():args.Key.ToString());input.Text=string.Join('+',modifiers);args.Handled=true;
        };
        var dialog=new ContentDialog{Title=T("設定快捷鍵","Set shortcut"),Content=Stack(input,Text(T("需含 Ctrl、Alt 或 Win；Backspace 可清除。","Include Ctrl, Alt or Win. Backspace clears."),12,true)),PrimaryButtonText=T("確認","Confirm"),CloseButtonText=T("取消","Cancel"),XamlRoot=fullRoot.XamlRoot,RequestedTheme=fullRoot.RequestedTheme};
        dialog.Opened+=(_,_)=>input.Focus(FocusState.Programmatic);
        StyleDialog(dialog);
        return await dialog.ShowAsync()==ContentDialogResult.Primary?input.Text:null;
    }
    private void GeneralPage()
    {
        var language=new ComboBox{MinWidth=150,ItemsSource=new[]{new Choice("system",T("跟隨系統","System")),new Choice("zh-Hant","繁體中文"),new Choice("en","English")},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=controller.Settings.Language};
        language.SelectionChanged+=async(_,_)=>{try{if(language.SelectedValue is string value&&value!=controller.Settings.Language){await controller.SaveAsync(s=>s with{Language=value});RenderAll();}}catch(Exception ex){Error(ex);}};
        var appearance=new ComboBox{MinWidth=150,ItemsSource=new[]{new Choice("system",T("跟隨系統","System")),new Choice("light",T("淺色","Light")),new Choice("dark",T("深色","Dark"))},DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=controller.Settings.Theme};
        appearance.SelectionChanged+=async(_,_)=>{try{if(appearance.SelectedValue is string value&&value!=controller.Settings.Theme){await controller.SaveAsync(s=>s with{Theme=value});RenderAll();}}catch(Exception ex){Error(ex);}};
        using var run=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        using var approved=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run");
        bool windowsDisabled=approved?.GetValue("AudioOutputManager") is byte[] bytes&&bytes.Length>0&&bytes[0]==3;
        bool actualLogin=run?.GetValue("AudioOutputManager") is string&&!windowsDisabled;
        var login=new ToggleSwitch{IsOn=actualLogin,OnContent=T("開啟","On"),OffContent=T("關閉","Off")};
        bool syncingLogin=false;
        login.Toggled+=async(_,_)=>
        {
            if(syncingLogin)return;
            bool next=login.IsOn;
            try
            {
                if(next&&windowsDisabled)throw new InvalidOperationException(T("Windows 已停用此啟動項目，請由 Windows 設定管理。","Windows disabled this startup entry. Manage it in Windows Settings."));
                using var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
                object? previousValue=key.GetValue("AudioOutputManager");
                if(next)key.SetValue("AudioOutputManager",$"\"{Environment.ProcessPath}\" --background");else key.DeleteValue("AudioOutputManager",false);
                try{await controller.SaveAsync(s=>s with{StartAtLogin=next});actualLogin=next;}catch{if(previousValue is string original)key.SetValue("AudioOutputManager",original);else key.DeleteValue("AudioOutputManager",false);throw;}
            }catch(Exception ex){Error(ex);syncingLogin=true;login.IsOn=actualLogin;syncingLogin=false;}
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(language,T("語言","Language"));Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(appearance,T("色彩主題","Theme"));Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(login,T("登入 Windows 時啟動","Start at Windows login"));
        pageHost.Children.Add(Card(Rows(Row(Stack(Text(T("語言","Language")),Text(T("可跟隨系統或指定顯示語言","Use the system language or choose one"),12,true)),language),Row(Stack(Text(T("色彩主題","Theme")),Text(T("快速選單與完整面板共用主題","Shared by the quick menu and full panel"),12,true)),appearance),Row(Stack(Text(T("登入 Windows 時啟動","Start at Windows login")),Text(T("只影響目前使用者","For the current user only"),12,true)),login))));
        if(windowsDisabled)pageHost.Children.Add(Text(T("Windows 已停用此啟動項目，本工具不會繞過。","This startup entry is disabled by Windows; the app will not bypass it."),12,true));
        pageHost.Children.Add(Card(Inset(Stack(Text(T("關閉與常駐","Closing and background operation"),16),Text(T("關窗後繼續常駐；只有結束程式才會停止。沒有背景更新查詢或遙測。","Closing the window keeps the app running. Exit stops it. No background update checks or telemetry."),14,true)))));
    }
    private sealed record Choice(string Value,string Label);
    private void AboutPage()
    {
        string version=typeof(ShellWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ??typeof(ShellWindow).Assembly.GetName().Version?.ToString(3)??"0.1.0";
        pageHost.Children.Add(Card(Row(Stack(Text("AuriHelm · "+version,16),Text(T("候選測試版；完整驗收尚未完成。","Release candidate; full acceptance is pending."),12,true)),Button(T("檢查更新","Check for updates"),()=>{OnIssue(T("尚未指定公開發佈儲存庫，未進行網路查詢。","No release repository configured; no network request made."));return Task.CompletedTask;}))));
        pageHost.Children.Add(Card(Rows(
            Row(Stack(Text(T("本機診斷","Local diagnostics"),16),Text(T("只保留必要事件碼，不含裝置識別碼、別名、路徑或音訊。","Only local event codes; no device IDs, aliases, paths or audio."),12,true)),Tag(T("僅本機","Local only"))),
            Row(Stack(Text(T("診斷紀錄","Diagnostic log")),Text(T("匯出前先預覽；由你選擇儲存位置。","Preview before export, then choose where to save."),12,true)),Actions(Button(T("清除紀錄","Clear logs"),()=>{diagnostics.Clear();OnIssue(T("已清除本機紀錄。","Local logs cleared."));return Task.CompletedTask;}),Button(T("預覽並匯出","Preview and export"),ExportDiagnostics))))));
        pageHost.Children.Add(Card(Disclosure("licenses",T("開放原始碼與授權","Open source and licenses"),Inset(Text(T("Copyright © 2026 Kerry (kkldream)。本專案採 MIT 授權；第三方著作權及授權見 THIRD-PARTY-NOTICES.md。","Copyright © 2026 Kerry (kkldream). Licensed under MIT. Third-party notices: THIRD-PARTY-NOTICES.md."),12,true)))));
    }
    private async Task ExportDiagnostics()
    {
        string preview=diagnostics.Preview();
        var dialog=new ContentDialog{Title=T("診斷匯出預覽","Diagnostic export preview"),Content=new TextBox{Text=preview,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=400,AcceptsReturn=true},PrimaryButtonText=T("選擇位置並儲存","Choose location and save"),CloseButtonText=T("取消","Cancel"),XamlRoot=fullRoot.XamlRoot};
        StyleDialog(dialog);
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var picker=new FileSavePicker{SuggestedFileName="audio-diagnostics"};picker.FileTypeChoices.Add("Text",new List<string>{".txt"});InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(full));
        var file=await picker.PickSaveFileAsync();if(file is not null)await global::Windows.Storage.FileIO.WriteTextAsync(file,preview);
    }
}
