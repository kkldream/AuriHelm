param([string]$SourceRef)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $repoRoot '.tools/nsis-3.13/makensis.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = (Get-Command makensis -ErrorAction Stop).Source }
if ((& $compiler /VERSION) -ne 'v3.13') { throw '需要 NSIS 3.13。' }
$runRoot = Join-Path $repoRoot ('artifacts/acceptance/installer-paths-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$source = if ($SourceRef) { (git -C $repoRoot show "${SourceRef}:installer/AudioOutputManager.nsi") -join "`n" } else { Get-Content -LiteralPath (Join-Path $repoRoot 'installer/AudioOutputManager.nsi') -Raw }
$match = [regex]::Match($source, '(?ms)^!macro VerifyPaths PREFIX\r?\n.*?^!macroend')
if (-not $match.Success) { throw '找不到產品 VerifyPaths，停止以免測到另一份實作。' }
$target = Join-Path $runRoot '首次 安裝/AudioOutputManager'
$cases = @(
    @{ Name='missing-target'; Expected=$target; Input=$target; Code=0 },
    @{ Name='existing-target'; Expected=$target; Input=$target; Code=0; Create=$true },
    @{ Name='empty-input'; Expected=$target; Input=''; Code=1 },
    @{ Name='relative-input'; Expected=$target; Input='.new'; Code=1 },
    @{ Name='different-absolute-input'; Expected=$target; Input=(Join-Path $runRoot 'other'); Code=1 },
    @{ Name='empty-expected'; Expected=''; Input=''; Code=1 },
    @{ Name='relative-expected'; Expected='.new'; Input='.new'; Code=1 }
)
$results = [Collections.Generic.List[object]]::new()
foreach ($case in $cases) {
    if ($case.Create) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
    $targetExisted = Test-Path -LiteralPath $target
    $macro = $match.Value.Replace('$LOCALAPPDATA\Programs\AudioOutputManager', $case.Expected)
    $resultFile = Join-Path $runRoot ($case.Name + '.txt')
    $exe = Join-Path $runRoot ($case.Name + '.exe')
    $script = @'
Unicode true
!include "LogicLib.nsh"
!include "FileFunc.nsh"
Name "AuriHelm path regression fixture"
OutFile "@EXE@"
RequestExecutionLevel user
SilentInstall silent
Var Expected
Var Stage
Var Previous
@MACRO@
!insertmacro VerifyPaths ""
Section
  StrCpy $INSTDIR "@INPUT@"
  Call VerifyPaths
  FileOpen $0 "@RESULT@" w
  FileWriteUTF16LE $0 "Expected=$Expected$\r$\nStage=$Stage$\r$\nPrevious=$Previous$\r$\n"
  FileClose $0
  ${If} $Expected != "@TARGET@"
    SetErrorLevel 10
  ${ElseIf} $Stage != "@TARGET@.new"
    SetErrorLevel 11
  ${ElseIf} $Previous != "@TARGET@.previous"
    SetErrorLevel 12
  ${Else}
    SetErrorLevel 0
  ${EndIf}
SectionEnd
'@
    $script = $script.Replace('@EXE@', $exe).Replace('@TARGET@', $target).Replace('@INPUT@', $case.Input).Replace('@RESULT@', $resultFile).Replace('@MACRO@', $macro)
    $scriptFile = Join-Path $runRoot ($case.Name + '.nsi')
    Set-Content -LiteralPath $scriptFile -Value $script -Encoding utf8
    & $compiler /V2 /WX /INPUTCHARSET UTF8 $scriptFile
    if ($LASTEXITCODE -ne 0) { throw '路徑 fixture 編譯失敗。' }
    $process = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(10000)) { $process.Kill(); throw '路徑 fixture 逾時。' }
    $process.Refresh()
    $actual = if (Test-Path -LiteralPath $resultFile) { Get-Content -LiteralPath $resultFile -Raw -Encoding unicode } else { '<rejected before output>' }
    $result = [pscustomobject]@{ Name=$case.Name; Passed=$process.ExitCode -eq $case.Code; ExitCode=$process.ExitCode; ExpectedCode=$case.Code; TargetExistedBefore=$targetExisted; Actual=$actual; Evidence=$runRoot }
    $results.Add($result)
    $result | ConvertTo-Json
    $results.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'results.json') -Encoding utf8
    if (-not $result.Passed) { throw "安裝路徑回歸失敗：$($case.Name)（$($process.ExitCode)）。" }
}
