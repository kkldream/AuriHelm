param([switch]$Publish, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
$publishPath = $null
if ($Publish) {
    [xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw '產品版本格式不符。' }
    if (-not $OutputDirectory) { $OutputDirectory = "artifacts/releases/v$version/app" }
    $publishPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
    if (-not $publishPath.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '輸出必須位於本專案 artifacts 內。' }
    if (Test-Path -LiteralPath $publishPath) { throw '輸出目錄已存在；請指定新的目錄，不覆寫既有產物。' }
    $parent = [IO.DirectoryInfo]::new($publishPath).Parent
    while ($parent -and $parent.FullName -ne $repoRoot) {
        if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '輸出路徑不能經過目錄連結。' }
        $parent = $parent.Parent
    }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnetPath = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$requiredVersion = (Get-Content -Raw global.json | ConvertFrom-Json).sdk.version
if ((& $dotnetPath --version) -ne $requiredVersion) { throw "需要 .NET SDK $requiredVersion，請依 global.json 安裝。" }
& $dotnetPath restore AudioOutputManager.sln --locked-mode --nologo
if ($LASTEXITCODE -ne 0) { throw '套件鎖定還原失敗。' }
& $dotnetPath build AudioOutputManager.sln -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw '建置失敗。' }
& $dotnetPath test tests/AudioOutputManager.Tests -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw '測試失敗。' }
if ($Publish) {
    & $dotnetPath publish src/AudioOutputManager.App -c Release -r win-x64 --self-contained true --no-restore -o $publishPath --nologo
    if ($LASTEXITCODE -ne 0) { throw '發佈目錄產生失敗。' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath 'AudioOutputManager.App.pri'))) { throw '發佈目錄缺少應用程式 PRI，已停止打包。' }
    Copy-Item -LiteralPath LICENSE,THIRD-PARTY-NOTICES.md -Destination $publishPath
    & (Join-Path $PSScriptRoot 'collect-licenses.ps1') -Destination $publishPath
    Get-FileHash (Join-Path $publishPath 'AudioOutputManager.App.exe'),(Join-Path $publishPath 'AudioOutputManager.App.dll') -Algorithm SHA256 | Format-List
    Write-Output '已產生本機測試目錄；尚未執行、安裝或公開發佈。'
}
