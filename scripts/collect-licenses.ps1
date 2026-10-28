param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$assets = Get-Content -Raw (Join-Path $repoRoot 'src/AudioOutputManager.App/obj/project.assets.json') | ConvertFrom-Json
$packageRoot = ($assets.packageFolders.PSObject.Properties | Select-Object -First 1).Name
$licenseDirectory = Join-Path $Destination 'third-party-licenses'
New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
$index = @()
$notice = Get-Content -Raw (Join-Path $repoRoot 'LICENSE')
$mitStart = $notice.IndexOf('Permission is hereby granted')
if ($mitStart -lt 0) { throw 'LICENSE 缺少 MIT 授權條文。' }
$mitBody = $notice.Substring($mitStart).Trim()
foreach ($entry in $assets.libraries.PSObject.Properties | Sort-Object Name) {
    if ($entry.Value.type -ne 'package') { continue }
    $base = [IO.Path]::GetFullPath((Join-Path $packageRoot $entry.Value.path))
    $nuspec = Get-ChildItem -LiteralPath $base -Filter '*.nuspec' | Select-Object -First 1
    if (-not $nuspec) { throw "套件缺少 nuspec：$($entry.Name)" }
    [xml]$xml = Get-Content -Raw -LiteralPath $nuspec.FullName
    $metadata = $xml.SelectSingleNode("//*[local-name()='metadata']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $id = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
    $version = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
    $copyright = $metadata.SelectSingleNode("*[local-name()='copyright']").InnerText
    $filename = "$id-$version.txt"
    $description = if ($license) { $license.InnerText } else { 'See license URL / package source' }
    if ($license -and $license.type -eq 'file') {
        $licensePath = [IO.Path]::GetFullPath((Join-Path $base $license.InnerText))
        if (-not $licensePath.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '套件授權路徑超出套件目錄。' }
        if (-not (Test-Path -LiteralPath $licensePath)) { throw "缺少授權原文：$id" }
        $filename = "$id-$version" + [IO.Path]::GetExtension($licensePath)
        Copy-Item -LiteralPath $licensePath -Destination (Join-Path $licenseDirectory $filename)
    } elseif ($description -eq 'MIT') {
        "MIT License`n`n$copyright`n`n$mitBody" | Set-Content -LiteralPath (Join-Path $licenseDirectory $filename) -Encoding utf8
    } else { $filename = $null }
    $index += [pscustomobject]@{
        package=$id;version=$version;license=$description;copyright=$copyright
        authors=$metadata.SelectSingleNode("*[local-name()='authors']").InnerText
        licenseUrl=$metadata.SelectSingleNode("*[local-name()='licenseUrl']").InnerText
        source=$metadata.SelectSingleNode("*[local-name()='projectUrl']").InnerText
        licenseFile=$filename
    }
}
$index | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $licenseDirectory 'packages.json') -Encoding utf8
Write-Output "已整理 $($index.Count) 個套件的版本與授權資訊（含建置用相依性）。"
