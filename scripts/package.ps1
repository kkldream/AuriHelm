param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
$fileVersion = [string]$properties.Project.PropertyGroup.FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$' -or $fileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw '產品版本格式不符。' }
if (-not $OutputDirectory) { $OutputDirectory = "artifacts/releases/v$version" }
$packagePath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not $packagePath.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '安裝包輸出必須位於本專案 artifacts 內。' }
if (Test-Path -LiteralPath $packagePath) { throw '輸出目錄已存在；請指定新的目錄，不覆寫既有產物。' }
$appPath = Join-Path $packagePath 'app'
& (Join-Path $PSScriptRoot 'test-installer-paths.ps1')
& (Join-Path $PSScriptRoot 'test-installer.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Publish -OutputDirectory $appPath
$compiler = Join-Path $repoRoot '.tools/nsis-3.13/makensis.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = (Get-Command makensis -ErrorAction Stop).Source }
if ((& $compiler /VERSION) -ne 'v3.13') { throw '需要 NSIS 3.13，請依 CONTRIBUTING.md 取得官方版本。' }
Copy-Item -LiteralPath (Join-Path (Split-Path $compiler) 'COPYING') -Destination (Join-Path $appPath 'third-party-licenses/NSIS-COPYING.txt')
$setupPath = Join-Path $packagePath "AuriHelm-$version-win-x64-setup.exe"
$label = if ($version.Contains('-')) { 'AuriHelm (候選測試版)' } else { 'AuriHelm' }
& $compiler /V2 /WX /INPUTCHARSET UTF8 "/DSOURCE_DIR=$appPath" "/DOUTPUT_FILE=$setupPath" "/DPRODUCT_VERSION=$version" "/DPRODUCT_FILE_VERSION=$fileVersion" "/DPRODUCT_LABEL=$label" installer/AudioOutputManager.nsi
if ($LASTEXITCODE -ne 0) { throw '安裝程式編譯失敗。' }
Get-FileHash -LiteralPath $setupPath -Algorithm SHA256 | Format-List
Write-Output '只產生本機測試安裝包；尚未執行、安裝、簽章或公開發佈。'
