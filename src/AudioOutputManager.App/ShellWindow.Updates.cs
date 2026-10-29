using AudioOutputManager.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AudioOutputManager.App;

internal sealed partial class ShellWindow
{
    private readonly ReleaseUpdateClient releaseUpdates=new();
    private readonly CancellationTokenSource updateCancellation=new();
    private UpdateCheckResult? updateResult;
    private bool updateChecking;
    private Button? updateButton;
    private TextBlock? updateMessage;
    private HyperlinkButton? updateLink;
    private FrameworkElement? updateDetails;

    private UIElement UpdateCard(string version)
    {
        updateButton=Button(T("檢查更新","Check for updates"),()=>CheckForUpdates(version));
        updateMessage=Text("",12,true);
        updateLink=new HyperlinkButton{Padding=new(0),Foreground=theme.Blue,HorizontalAlignment=HorizontalAlignment.Left,
            FocusVisualPrimaryBrush=theme.Blue,FocusVisualSecondaryBrush=theme.Head};
        updateDetails=new Microsoft.UI.Xaml.Controls.Border{Padding=new(16,0,16,14),Child=Stack(updateMessage,updateLink)};
        var content=new StackPanel();
        content.Children.Add(Row(Stack(Text("AuriHelm · "+version,16),Text(T("候選測試版；完整驗收尚未完成。","Release candidate; full acceptance is pending."),12,true)),updateButton));
        content.Children.Add(updateDetails);RefreshUpdateResult();
        return Card(content);
    }
    private async Task CheckForUpdates(string version)
    {
        if(updateChecking||quitting)return;
        updateChecking=true;RefreshUpdateResult();
        try {updateResult=await releaseUpdates.CheckAsync(version,updateCancellation.Token);}
        finally {updateChecking=false;if(!quitting)RefreshUpdateResult();}
    }
    private void RefreshUpdateResult()
    {
        if(updateButton is null||updateMessage is null||updateLink is null||updateDetails is null)return;
        updateButton.IsEnabled=!updateChecking;
        updateButton.Content=updateChecking?T("查詢中…","Checking…"):T("檢查更新","Check for updates");
        AutomationProperties.SetName(updateButton,updateButton.Content.ToString());
        updateDetails.Visibility=updateChecking||updateResult is not null?Visibility.Visible:Visibility.Collapsed;
        updateLink.Visibility=updateChecking||updateResult is null?Visibility.Collapsed:Visibility.Visible;
        if(updateChecking){updateMessage.Text=T("正在檢查正式版本…","Checking stable releases…");return;}
        if(updateResult is not {} result)return;
        updateMessage.Text=result.State switch
        {
            UpdateCheckState.Available=>T($"有新的正式版本：{result.Version}。",$"A new stable release is available: {result.Version}."),
            UpdateCheckState.UpToDate=>T($"目前沒有比本機更新的正式版本（最新正式版 {result.Version}）。",$"No stable release is newer than this version (latest stable: {result.Version})."),
            UpdateCheckState.NoStableRelease=>T("目前沒有可取得的正式版本；可前往版本頁查看候選版。","No stable release is available. The releases page may have release candidates."),
            UpdateCheckState.Offline=>T("無法連線至 GitHub，請檢查網路後重試。","Cannot connect to GitHub. Check your connection and retry."),
            UpdateCheckState.TimedOut=>T("查詢逾時，請稍後重試。","The update check timed out. Please retry later."),
            UpdateCheckState.RateLimited=>T("GitHub 暫時限制查詢次數，請稍後重試。","GitHub's request limit was reached. Please retry later."),
            UpdateCheckState.Unavailable=>T($"GitHub 暫時無法提供更新資訊（HTTP {result.HttpStatus}）。",$"GitHub cannot provide update information (HTTP {result.HttpStatus})."),
            UpdateCheckState.InvalidResponse=>T("版本資訊無效，無法判斷是否需要更新。","Release information is invalid; update availability could not be determined."),
            UpdateCheckState.InvalidCurrentVersion=>T("本機版本無法比較，請前往版本頁查看。","The local version cannot be compared. Please check the releases page."),
            _=>T("更新查詢已取消。","The update check was cancelled.")
        };
        updateLink.Content=result.State==UpdateCheckState.Available?T("前往下載","View download"):T("查看版本頁","View releases");
        AutomationProperties.SetName(updateLink,updateLink.Content.ToString());
        updateLink.NavigateUri=result.ReleasePage??ReleaseUpdateClient.VersionsPage;
    }
}
