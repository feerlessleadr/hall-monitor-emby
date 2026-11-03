. C:\dev\hall-monitor-emby\tools\Common.ps1
Initialize-Context
$log = (Join-Path $env:TEMP "push-reason.log"); Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
$apiKey = 'reason-test-key-0123456789abcdef'
$user = $null; $proc = $null
function Check($name, $ok, $detail = '') { if ($ok) { "[PASS] $name $detail" } else { "[FAIL] $name $detail"; $script:failed++ } }
$script:failed = 0
try {
  $user = New-ProbeUser -Name 'we-reason' -DeviceId 'we-reason-device'
  $cfg = Get-PluginConfig
  $cfg.EnableWatchTimeLimiter = $true; $cfg.EnablePublicApi = $true; $cfg.ApiKey = $apiKey
  $cfg | Add-Member -NotePropertyName LimitedUsers -NotePropertyValue @((New-LimitedUserEntry -User $user -DailyMinutes 60)) -Force
  Save-PluginConfig $cfg
  $h = $script:Admin + @{ 'X-HallMonitor-Api-Key' = $apiKey }
  $proc = Start-PushClient -Token $script:Admin['X-Emby-Token'] -LogFile $log -PluginKey $apiKey
  Start-Sleep 4
  function Post($path, $body) { Invoke-RestMethod "$($script:Api)$path" -Method Post -Headers $h -ContentType 'application/json' -Body ($body | ConvertTo-Json) | Out-Null }
  function Status() { (Invoke-RestMethod "$($script:Api)/HallMonitor/Public/Status" -Headers $h) | Where-Object { $_.UserId -eq $user.Id } }
  $before = (Get-StatusPushes $log).Count
  $t0 = Get-Date
  Post '/HallMonitor/Public/ExtendTime' @{ UserId = $user.Id; Minutes = 15; Source = 'homeassistant'; Reason = 'Feed Chloe' }
  Start-Sleep -Milliseconds 1500
  $pushes = @(Get-StatusPushes $log)
  Check 'A push arrives right after ExtendTime' ($pushes.Count -gt $before) "pushes $before -> $($pushes.Count)"
  $last = $pushes[-1].Users | Where-Object { $_.UserId -eq $user.Id }
  $c = @($last.RecentTimeChanges)[-1]
  Check 'The push itself carries the reason' ($c.Reason -eq 'Feed Chloe' -and $c.Source -eq 'homeassistant' -and $c.Minutes -eq 15 -and $c.Kind -eq 'Added') ($c | ConvertTo-Json -Compress)
  Post '/HallMonitor/Public/ExtendTime' @{ UserId = $user.Id; Minutes = 30; Source = 'app' }
  Post '/HallMonitor/Public/ReduceTime' @{ UserId = $user.Id; Minutes = 10; Source = 'homeassistant'; Reason = 'Traded for play time' }
  Post '/HallMonitor/Public/ExtendTime' @{ UserId = $user.Id; Minutes = 5 }
  Post '/HallMonitor/Public/ExtendTime' @{ UserId = $user.Id; Minutes = 1; Source = '<script>'; Reason = ("line1`r`nline2 " + ('x' * 200) + ' "quoted"') }
  Invoke-RestMethod "$($script:Api)/HallMonitor/ExtendTime" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body (@{ UserId = $user.Id; Minutes = 2; Reason = 'from plugin page' } | ConvertTo-Json) | Out-Null
  Invoke-RestMethod "$($script:Api)/HallMonitor/ReduceTime" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body (@{ UserId = $user.Id; Minutes = 1 } | ConvertTo-Json) | Out-Null
  $s = Status
  $list = @($s.RecentTimeChanges)
  Check 'Status says ApiLevel 7' ($s.ApiLevel -eq 7) "api=$($s.ApiLevel)"
  Check 'Seven changes listed, oldest first, ids growing' ($list.Count -eq 7 -and (($list | ForEach-Object { [int64]$_.Id }) -join ',') -eq (($list | ForEach-Object { [int64]$_.Id } | Sort-Object) -join ',')) "count=$($list.Count)"
  Check 'App add has source app and no reason' ($list[1].Source -eq 'app' -and $list[1].Reason -eq '' -and $list[1].Minutes -eq 30)
  Check 'HA reduction is negative and Removed with its reason' ($list[2].Minutes -eq -10 -and $list[2].Kind -eq 'Removed' -and $list[2].Reason -eq 'Traded for play time')
  Check 'Old-style call (no source) is unknown with no reason' ($list[3].Source -eq 'unknown' -and $list[3].Reason -eq '' -and $list[3].Minutes -eq 5)
  Check 'Odd source becomes other; reason is one line, capped at 80' ($list[4].Source -eq 'other' -and $list[4].Reason.Length -le 80 -and $list[4].Reason -notmatch "[\r\n]") "len=$($list[4].Reason.Length)"
  Check 'Plugin-page add is recorded as plugin with its reason' ($list[5].Source -eq 'plugin' -and $list[5].Reason -eq 'from plugin page')
  Check 'Plugin-page reduce is recorded as plugin' ($list[6].Source -eq 'plugin' -and $list[6].Minutes -eq -1)
  $expectedCredit = (15 + 30 - 10 + 5 + 1 + 2 - 1) * 60
  Check 'Credit still adds up (behaviour unchanged)' ([math]::Abs($s.CreditSecondsDaily - $expectedCredit) -lt 1) "credit=$($s.CreditSecondsDaily) expected=$expectedCredit"
  Check 'Bad request still refused (0 minutes)' ((Get-Status 'POST' '/HallMonitor/Public/ExtendTime' $h @{ UserId = $user.Id; Minutes = 0; Reason = 'x' }) -eq 400)
  $n = @((Get-StatusPushes $log)[-1].Users | Where-Object { $_.UserId -eq $user.Id }).RecentTimeChanges.Count
  Check 'The latest push shows the same seven changes' ($n -eq 7) "n=$n"
  $firstPushMs = ((Get-Date) - $t0).TotalSeconds
  # Save the real pushed payload for the app test
  $raw = Get-Content -LiteralPath $log | Where-Object { $_ -match '"MessageType":"HallMonitorStatus"' } | Select-Object -Last 1
  ($raw -replace '^\S+ ', '') | Set-Content -LiteralPath (Join-Path $env:TEMP "real-push.json") -Encoding utf8
} finally {
  if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  Reset-PluginState
  Remove-ProbeUser $user
}
"failed: $($script:failed)"


