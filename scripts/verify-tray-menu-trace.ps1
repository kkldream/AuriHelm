param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [int]$MinimumRequests=3,
    [int]$ProcessId=0
)
$ErrorActionPreference='Stop'
$events=@(Get-Content -LiteralPath $InputPath | Where-Object { $_.Trim().Length } | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object Kind -EQ 'DEBUG-tray-reopen')
if(-not $events.Count){throw '沒有右鍵診斷事件。'}
if(-not $ProcessId){$ProcessId=$events[-1].Process}
$events=@($events | Where-Object Process -EQ $ProcessId)
$requests=@($events | Where-Object Stage -EQ 'show-request')
$failures=[Collections.Generic.List[string]]::new()
if($requests.Count-lt$MinimumRequests){$failures.Add("只有 $($requests.Count) 次請求，需要至少 $MinimumRequests 次。")}
$results=foreach($request in $requests)
{
    $related=@($events | Where-Object Request -EQ $request.Request)
    $opened=$related | Where-Object Stage -EQ 'popup-opened' | Select-Object -First 1
    $errors=@($related | Where-Object Stage -EQ 'show-error')
    if(-not $opened){$failures.Add("請求 $($request.Request) 沒有 popup-opened。")}
    if($errors.Count){$failures.Add("請求 $($request.Request) 有顯示錯誤。")}
    [pscustomobject]@{
        Request=$request.Request
        Opened=$null-ne$opened
        Milliseconds=if($opened){[Math]::Round(($opened.Ticks-$request.Ticks)*1000.0/$request.Frequency,3)}else{$null}
        Stages=@($related.Stage)
    }
}
[pscustomobject]@{Passed=$failures.Count-eq0;Process=$ProcessId;Requests=$requests.Count;Opened=@($results|Where-Object Opened).Count;Failures=$failures.ToArray();Results=@($results)} | ConvertTo-Json -Depth 6
if($failures.Count){exit 1}
