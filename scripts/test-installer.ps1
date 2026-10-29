param([string]$SourceRef, [string]$Case='*', [switch]$UseCurrentPathValidation)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $repoRoot '.tools/nsis-3.13/makensis.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = (Get-Command makensis -ErrorAction Stop).Source }
if ((& $compiler /VERSION) -ne 'v3.13') { throw '需要 NSIS 3.13。' }
$source = if ($SourceRef) { (git -C $repoRoot show "${SourceRef}:installer/AudioOutputManager.nsi") -join "`n" } else { Get-Content -LiteralPath (Join-Path $repoRoot 'installer/AudioOutputManager.nsi') -Raw }
if ($UseCurrentPathValidation) {
    if (-not $SourceRef) { throw '只在比較舊交易流程時指定 UseCurrentPathValidation。' }
    $current = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/AudioOutputManager.nsi') -Raw
    $pattern = '(?ms)^!macro VerifyPaths PREFIX\r?\n.*?^!macroend'
    $currentPaths = [regex]::Match($current, $pattern).Value
    if (-not $currentPaths) { throw '找不到目前路徑函式。' }
    $source = [regex]::Replace($source, $pattern, [Text.RegularExpressions.MatchEvaluator]{ param($m) $currentPaths })
    $source = "!include `"FileFunc.nsh`"`n" + $source
}
$runRoot = Join-Path $repoRoot ('artifacts/acceptance/installer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$results = [Collections.Generic.List[object]]::new()
$ownedKeys = [Collections.Generic.List[string]]::new()
$testParent = 'Software\AuriHelm.InstallerTests'
$existingParent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testParent)
$parentExisted = $null -ne $existingParent
if ($existingParent) { $existingParent.Dispose() }

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-Registration([string]$Key) {
    $entry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Key)
    if (-not $entry) { return '<absent>' }
    try {
        $snapshot = foreach ($name in ($entry.GetValueNames() | Sort-Object)) {
            [pscustomobject]@{ Name=$name; Kind=$entry.GetValueKind($name).ToString(); Value=$entry.GetValue($name) }
        }
        return ConvertTo-Json -InputObject @($snapshot) -Compress -Depth 4
    } finally { $entry.Dispose() }
}

function Get-Tree([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return '<absent>' }
    $files = Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path=[IO.Path]::GetRelativePath($Path, $_.FullName); SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    }
    return ConvertTo-Json -InputObject @($files) -Compress
}

function New-Fixture([string]$Name, [string]$FinishChoice='') {
    $id = [Guid]::NewGuid().ToString('N')
    $root = Join-Path $runRoot $Name
    $target = Join-Path $root '測試 安裝/Programs/AudioOutputManager'
    $data = Join-Path $root '測試 資料/AudioOutputManager'
    $links = Join-Path $root 'Shortcuts'
    $desktop = Join-Path $root 'Desktop'
    $key = "Software\AuriHelm.InstallerTests\$id"
    $ownedKeys.Add($key)
    New-Item -ItemType Directory -Path $root,$data,$links,$desktop | Out-Null
    Set-Content -LiteralPath (Join-Path $data 'settings.json') -Value '{"alias":"保留別名","excluded":["chrome.exe"],"priority":["headset","speakers"]}' -Encoding utf8
    Set-Content -LiteralPath (Join-Path $data 'export.json') -Value 'user-owned export' -Encoding utf8
    $fixtureSource = $source.Replace('$LOCALAPPDATA\Programs\AudioOutputManager', $target).
        Replace('$LOCALAPPDATA\AudioOutputManager', $data).
        Replace('$LOCALAPPDATA\Programs\..\AudioOutputManager', $data).
        Replace('$SMPROGRAMS\AuriHelm.lnk', (Join-Path $links 'AuriHelm.lnk')).
        Replace('$SMPROGRAMS\Audio Output Manager.lnk', (Join-Path $links 'Audio Output Manager.lnk')).
        Replace('$DESKTOP\AuriHelm.lnk', (Join-Path $desktop 'AuriHelm.lnk')).
        Replace('Software\Microsoft\Windows\CurrentVersion\Uninstall\AudioOutputManager', $key).
        Replace('"Software\Microsoft\Windows\CurrentVersion\Run" "AudioOutputManager"', ('"Software\Microsoft\Windows\CurrentVersion\Run" "AuriHelm.InstallerTests.' + $id + '"')).
        Replace('Local\AudioOutputManager.$0', ('Local\AuriHelm.InstallerTests.' + $id + '.$0')).
        Replace('"..\src\AudioOutputManager.App\Assets\AuriHelm.ico"', ('"' + (Join-Path $repoRoot 'src/AudioOutputManager.App/Assets/AuriHelm.ico') + '"'))
    # Run the production transaction and callbacks in isolated namespaces. The
    # fake payload is never executed: replace only the final launch with a marker.
    # A selected finish callback is invoked without displaying the wizard.
    if ($fixtureSource.Contains('Function LaunchApplication')) {
        $launch = 'Exec ''"$INSTDIR\AudioOutputManager.App.exe"'''
        Assert-That ($fixtureSource.Contains($launch)) '找不到正式完成頁的啟動命令。'
        $stub = 'FileOpen $0 "' + (Join-Path $root 'launch-requested.txt') + '" w' + "`n  FileWrite `$0 `"launch requested`"`n  FileClose `$0"
        $fixtureSource = $fixtureSource.Replace($launch, $stub).
            Replace('MessageBox MB_ICONEXCLAMATION "$(DesktopShortcutFailed)" /SD IDOK', 'DetailPrint "$(DesktopShortcutFailed)"')
    }
    $finishActions = switch ($FinishChoice) {
        'desktop' { "  SetSilent normal`n  Call CreateDesktopShortcut`n  SetSilent silent" }
        'desktop-first-only' {
@'
!if "${PRODUCT_VERSION}" == "0.0.1-fixture"
  SetSilent normal
  Call CreateDesktopShortcut
  SetSilent silent
!endif
'@
        }
        'launch' { "  SetSilent normal`n  Call LaunchApplication`n  SetSilent silent" }
        'silent-callbacks' { "  Call LaunchApplication`n  Call CreateDesktopShortcut" }
        '' { '' }
        default { throw '未知的完成頁測試選項。' }
    }
    if ($finishActions) {
        Assert-That ($fixtureSource.Contains('Function LaunchApplication')) '此來源尚未實作完成頁。'
        $fixtureSource = [regex]::Replace($fixtureSource, '(?m)^  done:\r?$', "  done:`n$finishActions")
    }
    $fixtureSource = [regex]::Replace($fixtureSource, '(?m)^(\s*MessageBox [^\r\n]*")\r?$', '$1 /SD IDOK')
    foreach ($forbidden in @('$LOCALAPPDATA\Programs\AudioOutputManager', '$LOCALAPPDATA\AudioOutputManager', '$SMPROGRAMS\AuriHelm.lnk', '$DESKTOP', 'CurrentVersion\Uninstall\AudioOutputManager', 'Local\AudioOutputManager.$0', '"Run" "AudioOutputManager"')) {
        if ($fixtureSource.Contains($forbidden)) { throw "fixture 仍包含產品命名空間：$forbidden" }
    }
    $fixtureSource = "SilentInstall silent`nSilentUnInstall silent`n" + $fixtureSource
    $nsi = Join-Path $root 'fixture.nsi'
    Set-Content -LiteralPath $nsi -Value $fixtureSource -Encoding utf8
    $executables = @{}
    foreach ($version in @('0.0.1-fixture','0.0.2-fixture')) {
        $payload = Join-Path $root $version
        New-Item -ItemType Directory -Path (Join-Path $payload 'Assets') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $payload 'AudioOutputManager.App.exe') -Value "fixture payload $version; never executed" -Encoding utf8
        Set-Content -LiteralPath (Join-Path $payload 'AudioOutputManager.App.deps.json') -Value "{`"version`":`"$version`"}" -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $repoRoot 'src/AudioOutputManager.App/Assets/AuriHelm.ico') -Destination (Join-Path $payload 'Assets/AuriHelm.ico')
        $exe = Join-Path $root "$version.exe"
        $fileVersion = if ($version -eq '0.0.1-fixture') { '0.0.1.0' } else { '0.0.2.0' }
        $compileOutput = & $compiler /V2 /WX /INPUTCHARSET UTF8 "/DSOURCE_DIR=$payload" "/DOUTPUT_FILE=$exe" "/DPRODUCT_VERSION=$version" "/DPRODUCT_FILE_VERSION=$fileVersion" '/DPRODUCT_LABEL=AuriHelm installer regression fixture' $nsi 2>&1
        $compileOutput | Set-Content -LiteralPath (Join-Path $root "$version-compile.txt") -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw "fixture 編譯失敗：$compileOutput" }
        $executables[$version] = $exe
    }
    return [pscustomobject]@{ Root=$root; Target=$target; Data=$data; Link=(Join-Path $links 'AuriHelm.lnk'); DesktopLink=(Join-Path $desktop 'AuriHelm.lnk'); LaunchMarker=(Join-Path $root 'launch-requested.txt'); Key=$key; Executables=$executables }
}

function Invoke-Install($Fixture, [string]$Version, [string]$Arguments='/S') {
    $process = Start-Process -FilePath $Fixture.Executables[$Version] -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(20000)) {
        $process.Kill()
        throw '隔離 NSIS fixture 逾時；不操作任何桌面視窗。'
    }
    $process.Refresh()
    return $process.ExitCode
}

function Install-Old($Fixture) {
    Assert-That ((Invoke-Install $Fixture '0.0.1-fixture') -eq 0) '前置舊版安裝失敗。'
    Assert-That (Test-Path -LiteralPath (Join-Path $Fixture.Target 'AudioOutputManager.App.deps.json')) '前置安裝缺少必要檔案。'
}

function Invoke-Uninstall($Fixture) {
    $uninstaller = Join-Path $Fixture.Target 'Uninstall.exe'
    Assert-That ([IO.Path]::GetFullPath($uninstaller).StartsWith($runRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) '拒絕執行隔離範圍外的解除安裝程式。'
    $process = Start-Process -FilePath $uninstaller -ArgumentList "/S _?=$($Fixture.Target)" -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(20000)) { $process.Kill(); throw '隔離解除安裝 fixture 逾時。' }
    $process.Refresh()
    Assert-That ($process.ExitCode -eq 0) '隔離解除安裝失敗。'
    Assert-That (-not (Test-Path -LiteralPath (Join-Path $Fixture.Target 'AudioOutputManager.App.exe'))) '解除安裝未移除隔離程式。'
    Assert-That ((Get-Registration $Fixture.Key) -eq '<absent>') '解除安裝留下隔離登錄。'
}

function Assert-Original($Fixture, $Files, $Registration, $LinkHash) {
    [pscustomobject]@{ Before=[pscustomobject]@{ Files=$Files; Registration=$Registration; LinkSHA256=$LinkHash }; After=[pscustomobject]@{ Files=(Get-Tree $Fixture.Target); Registration=(Get-Registration $Fixture.Key); LinkSHA256=(Get-FileHash -LiteralPath $Fixture.Link).Hash } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Fixture.Root 'rollback-comparison.json') -Encoding utf8
    Assert-That ((Get-Tree $Fixture.Target) -eq $Files) '失敗後原程式目錄不一致。'
    Assert-That ((Get-Registration $Fixture.Key) -eq $Registration) '失敗後解除安裝登錄未還原。'
    Assert-That ((Get-FileHash -LiteralPath $Fixture.Link -Algorithm SHA256).Hash -eq $LinkHash) '失敗後原捷徑未還原。'
    Assert-That ((Get-Content -LiteralPath (Join-Path $Fixture.Data 'settings.json') -Raw) -like '*保留別名*') '設定被破壞。'
    Assert-That ((Get-Content -LiteralPath (Join-Path $Fixture.Data 'export.json') -Raw).Trim() -eq 'user-owned export') '使用者匯出被破壞。'
}

function Test-Case([string]$Name, [scriptblock]$Body, [string]$FinishChoice='') {
    if ($Name -notlike $Case) { return }
    $fixture = New-Fixture $Name $FinishChoice
    try { & $Body $fixture } catch {
        $results.Add([pscustomobject]@{ Name=$Name; Passed=$false; Root=$fixture.Root; Error=$_.Exception.Message })
        throw
    }
    $results.Add([pscustomobject]@{ Name=$Name; Passed=$true; Root=$fixture.Root })
    Write-Output "通過：$Name"
}

try {
    Test-Case 'first-install-unicode-missing-parent' {
        param($f)
        Assert-That (-not (Test-Path -LiteralPath (Split-Path -Parent $f.Target))) '前置條件錯誤：首次安裝父目錄已存在。'
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '首次安裝失敗。'
        Assert-That ((Get-Content -LiteralPath (Join-Path $f.Target 'AudioOutputManager.App.deps.json') -Raw) -like '*0.0.2-fixture*') '首次安裝缺少正確 payload。'
        Assert-That ((Get-Registration $f.Key) -like '*0.0.2-fixture*') '版本登錄錯誤。'
        Assert-That (Test-Path -LiteralPath $f.Link) '缺少捷徑。'
        # The pre-upgrade copy is an intentional recovery artifact, not migration.
        Assert-That ((Get-Content -LiteralPath (Join-Path $f.Data 'settings.pre-upgrade.json') -Raw) -eq (Get-Content -LiteralPath (Join-Path $f.Data 'settings.json') -Raw)) '設定備份不同。'
    }
    Test-Case 'upgrade-preserves-settings' {
        param($f)
        Install-Old $f
        $oldFiles = Get-Tree $f.Target
        $dataBefore = Get-Tree $f.Data
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '升級失敗。'
        Assert-That ((Get-Tree "$($f.Target).previous") -eq $oldFiles) '上一版復原目錄不完整。'
        Assert-That ((Get-Tree $f.Data) -eq $dataBefore) '升級更動設定或使用者匯出。'
        Assert-That ((Get-Registration $f.Key) -like '*0.0.2-fixture*') '升級後版本登錄錯誤。'
    }
    Test-Case 'reject-other-install-dir' {
        param($f)
        $before = Get-Tree $f.Data
        $other = Join-Path $f.Root 'wrong-target'
        Assert-That ((Invoke-Install $f '0.0.2-fixture' "/S /D=$other") -ne 0) '未拒絕範圍外安裝路徑。'
        Assert-That (-not (Test-Path -LiteralPath $f.Target)) '拒絕後仍建立安裝目錄。'
        Assert-That ((Get-Registration $f.Key) -eq '<absent>') '拒絕後仍寫入登錄。'
        Assert-That ((Get-Tree $f.Data) -eq $before) '拒絕後更動使用者資料。'
    }
    Test-Case 'staging-failure-preserves-old' {
        param($f)
        Install-Old $f
        $files = Get-Tree $f.Target
        $registration = Get-Registration $f.Key
        $linkHash = (Get-FileHash -LiteralPath $f.Link).Hash
        Set-Content -LiteralPath "$($f.Target).new" -Value 'blocked by file' -Encoding utf8
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -ne 0) '未回報準備失敗。'
        Assert-Original $f $files $registration $linkHash
    }
    Test-Case 'locked-old-files-preserve-old' {
        param($f)
        Install-Old $f
        $files = Get-Tree $f.Target
        $registration = Get-Registration $f.Key
        $linkHash = (Get-FileHash -LiteralPath $f.Link).Hash
        $handle = [IO.File]::Open((Join-Path $f.Target 'AudioOutputManager.App.exe'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { Assert-That ((Invoke-Install $f '0.0.2-fixture') -ne 0) '未回報舊版檔案鎖定。' } finally { $handle.Dispose() }
        Assert-Original $f $files $registration $linkHash
    }
    Test-Case 'shortcut-failure-restores-files-and-registration' {
        param($f)
        Install-Old $f
        $entry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($f.Key, $true)
        try { $entry.SetValue('PreserveForeignValue', 'keep'); $entry.DeleteValue('NoRepair') } finally { $entry.Dispose() }
        $files = Get-Tree $f.Target
        $registration = Get-Registration $f.Key
        $linkHash = (Get-FileHash -LiteralPath $f.Link).Hash
        $handle = [IO.File]::Open($f.Link, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { $exitCode = Invoke-Install $f '0.0.2-fixture' } finally { $handle.Dispose() }
        Assert-Original $f $files $registration $linkHash
        Assert-That ($exitCode -eq 1) '捷徑鎖定未回報可回復的安裝失敗。'
        Assert-That (-not (Test-Path -LiteralPath "$($f.Link).new")) '留下暫存捷徑。'
    }
    Test-Case 'directory-link-is-rejected' {
        param($f)
        $outside = Join-Path $f.Root 'keep'
        New-Item -ItemType Directory -Path $outside,(Split-Path -Parent $f.Target) -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $outside 'keep.txt') -Value 'do not remove'
        New-Item -ItemType Junction -Path "$($f.Target).new" -Target $outside | Out-Null
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -ne 0) '未拒絕目錄連結。'
        Assert-That ((Get-Content -LiteralPath (Join-Path $outside 'keep.txt') -Raw).Trim() -eq 'do not remove') '目錄連結外的檔案遭改動。'
        Assert-That ((Get-Registration $f.Key) -eq '<absent>') '拒絕連結後仍寫入登錄。'
    }
    Test-Case 'unexpected-registration-type-is-preserved' {
        param($f)
        Install-Old $f
        $entry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($f.Key, $true)
        try { $entry.SetValue('DisplayVersion', [byte[]]@(1,2,3), [Microsoft.Win32.RegistryValueKind]::Binary) } finally { $entry.Dispose() }
        $files = Get-Tree $f.Target
        $registration = Get-Registration $f.Key
        $linkHash = (Get-FileHash -LiteralPath $f.Link).Hash
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -ne 0) '未拒絕無法完整還原的登錄型別。'
        Assert-Original $f $files $registration $linkHash
    }
    Test-Case 'unrelated-install-directory-is-preserved' {
        param($f)
        New-Item -ItemType Directory -Path $f.Target -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $f.Target 'keep.txt') -Value 'not an app installation'
        $files = Get-Tree $f.Target
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -ne 0) '未拒絕不含產品程式的既有目錄。'
        Assert-That ((Get-Tree $f.Target) -eq $files) '改動不屬於完整安裝的既有目錄。'
        Assert-That ((Get-Registration $f.Key) -eq '<absent>') '拒絕後仍留下登錄。'
    }
    Test-Case 'first-install-shortcut-failure-leaves-no-registration' {
        param($f)
        Set-Content -LiteralPath $f.Link -Value 'original unrelated shortcut' -Encoding utf8
        $hash = (Get-FileHash -LiteralPath $f.Link).Hash
        $handle = [IO.File]::Open($f.Link, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 1) '首次安裝捷徑失敗未正確回報。' } finally { $handle.Dispose() }
        Assert-That (-not (Test-Path -LiteralPath $f.Target)) '首次安裝失敗仍留下啟用的程式目錄。'
        Assert-That ((Get-Registration $f.Key) -eq '<absent>') '首次安裝失敗留下解除安裝登錄。'
        Assert-That ((Get-FileHash -LiteralPath $f.Link).Hash -eq $hash) '首次安裝失敗破壞原捷徑。'
    }
    Test-Case 'silent-install-does-not-launch-or-create-desktop' {
        param($f)
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '靜默安裝失敗。'
        Assert-That (-not (Test-Path -LiteralPath $f.LaunchMarker)) '靜默安裝啟動程式。'
        Assert-That (-not (Test-Path -LiteralPath $f.DesktopLink)) '靜默安裝建立桌面捷徑。'
    } 'silent-callbacks'
    Test-Case 'finish-launch-runs-only-selected-callback' {
        param($f)
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '選擇啟動的安裝失敗。'
        Assert-That (Test-Path -LiteralPath $f.LaunchMarker) '選擇啟動後未執行回呼。'
        Assert-That (-not (Test-Path -LiteralPath $f.DesktopLink)) '未選桌面捷徑仍建立。'
    } 'launch'
    Test-Case 'desktop-opt-in-unmodified-link-is-removed' {
        param($f)
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '選擇桌面捷徑的安裝失敗。'
        Assert-That (Test-Path -LiteralPath $f.DesktopLink) '未建立桌面捷徑。'
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq (Get-FileHash -LiteralPath (Join-Path $f.Target 'desktop-shortcut.lnk')).Hash) '桌面捷徑與擁有證據不同。'
        Assert-That (-not (Test-Path -LiteralPath $f.LaunchMarker)) '只選桌面捷徑卻啟動程式。'
        Invoke-Uninstall $f
        Assert-That (-not (Test-Path -LiteralPath $f.DesktopLink)) '未移除本程式建立的未改動捷徑。'
        Assert-That (Test-Path -LiteralPath (Join-Path $f.Data 'settings.json')) '解除安裝未保留設定。'
    } 'desktop'
    Test-Case 'desktop-existing-name-is-never-overwritten' {
        param($f)
        Set-Content -LiteralPath $f.DesktopLink -Value 'user-owned same-name file'
        $hash = (Get-FileHash -LiteralPath $f.DesktopLink).Hash
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '同名桌面檔案阻止安裝。'
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq $hash) '覆寫使用者既有同名檔案。'
        Assert-That (-not (Test-Path -LiteralPath (Join-Path $f.Target 'desktop-shortcut.lnk'))) '錯誤認領既有同名檔案。'
        Invoke-Uninstall $f
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq $hash) '解除安裝刪除非本程式建立的檔案。'
    } 'desktop'
    Test-Case 'desktop-same-length-edited-link-is-preserved' {
        param($f)
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '前置安裝失敗。'
        $bytes = [IO.File]::ReadAllBytes($f.DesktopLink)
        $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
        [IO.File]::WriteAllBytes($f.DesktopLink, $bytes)
        $hash = (Get-FileHash -LiteralPath $f.DesktopLink).Hash
        Invoke-Uninstall $f
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq $hash) '刪除已被使用者修改、但長度相同的捷徑。'
    } 'desktop'
    Test-Case 'desktop-ownership-survives-silent-upgrade' {
        param($f)
        Install-Old $f
        $hash = (Get-FileHash -LiteralPath $f.DesktopLink).Hash
        Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 0) '升級失敗。'
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq $hash) '升級覆寫既有捷徑。'
        Assert-That ((Get-FileHash -LiteralPath (Join-Path $f.Target 'desktop-shortcut.lnk')).Hash -eq $hash) '升級遺失擁有證據。'
        Invoke-Uninstall $f
        Assert-That (-not (Test-Path -LiteralPath $f.DesktopLink)) '升級後無法移除未改動捷徑。'
    } 'desktop-first-only'
    Test-Case 'desktop-reference-survives-upgrade-rollback' {
        param($f)
        Install-Old $f
        $files = Get-Tree $f.Target
        $registration = Get-Registration $f.Key
        $linkHash = (Get-FileHash -LiteralPath $f.Link).Hash
        $desktopHash = (Get-FileHash -LiteralPath $f.DesktopLink).Hash
        $handle = [IO.File]::Open($f.Link, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { Assert-That ((Invoke-Install $f '0.0.2-fixture') -eq 1) '鎖定捷徑未觸發升級回復。' } finally { $handle.Dispose() }
        Assert-Original $f $files $registration $linkHash
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq $desktopHash) '升級回復改動桌面捷徑。'
        Invoke-Uninstall $f
        Assert-That (-not (Test-Path -LiteralPath $f.DesktopLink)) '回復後遺失原捷徑擁有證據。'
    } 'desktop'
    Test-Case 'unowned-desktop-link-is-preserved-on-uninstall' {
        param($f)
        Install-Old $f
        Set-Content -LiteralPath $f.DesktopLink -Value 'user-created later'
        $hash = (Get-FileHash -LiteralPath $f.DesktopLink).Hash
        Invoke-Uninstall $f
        Assert-That ((Get-FileHash -LiteralPath $f.DesktopLink).Hash -eq $hash) '解除安裝刪除使用者後來建立的捷徑。'
    }
} finally {
    foreach ($key in $ownedKeys) {
        if ($key -notmatch '^Software\\AuriHelm\.InstallerTests\\[a-f0-9]{32}$') { throw '拒絕清理非本輪測試登錄。' }
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($key, $false)
    }
    if (-not $parentExisted) {
        $parent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testParent)
        if ($parent) {
            try { $empty = $parent.SubKeyCount -eq 0 -and $parent.ValueCount -eq 0 } finally { $parent.Dispose() }
            if ($empty) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($testParent, $false) }
        }
    }
    [pscustomobject]@{ SourceRef=$SourceRef; CurrentPathValidation=[bool]$UseCurrentPathValidation; Passed=@($results | Where-Object Passed).Count; Failed=@($results | Where-Object { -not $_.Passed }).Count; Results=$results.ToArray(); Evidence=$runRoot; Note='執行真 NSIS；使用隔離命名空間及假 payload，不代表完整 App 安裝／啟動驗收。' } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'results.json') -Encoding utf8
    Write-Output "證據：$runRoot"
}
