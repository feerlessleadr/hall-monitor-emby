# End-to-end regression checks for the Watching Eye plugin against a TEST Emby server.
#
#   powershell -ExecutionPolicy Bypass -File tools\Run-RegressionTests.ps1 [-BaseUrl http://localhost:8096] [-KeyFile <path>] [-SkipEnforcement]
#
# WARNING: run only against a test server. The script creates a throw-away user, changes the plugin
# configuration, and restores the plugin to a clean disabled state (and deletes the user) when it finishes.
# The Public API lockout test leaves the lockout active for 5 minutes (or until Emby restarts); use -SkipLockout to skip it.
# Takes about 2 minutes (the enforcement check plays a simulated 1-minute limit).
param(
    [string]$BaseUrl = 'http://localhost:8096',
    [string]$KeyFile = (Join-Path $env:USERPROFILE '.emby-spike-key'),
    [switch]$SkipEnforcement,
    [switch]$SkipLockout
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
Initialize-Context -BaseUrl $BaseUrl -KeyFile $KeyFile

$results = New-Object System.Collections.Generic.List[object]
function Check {
    param([string]$Name, [bool]$Passed, [string]$Detail = '')
    $results.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    $tag = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1} {2}" -f $tag, $Name, $Detail)
}
function Get-LimitedStatus { param($User) @(Invoke-RestMethod "$($script:Api)/WatchingEye/LimitedUsersStatus" -Headers $script:Admin) | ForEach-Object { $_ } | Where-Object { $_.UserId -eq $User.Id } | Select-Object -First 1 }

$probe = $null
$probe2 = $null
$listener = $null
$logFile = Join-Path ([IO.Path]::GetTempPath()) ("we-ws-{0}.log" -f [guid]::NewGuid().ToString('N'))
try {
    Write-Host "== Setup"
    $plugin = Invoke-RestMethod "$($script:Api)/Plugins" -Headers $script:Admin | ForEach-Object { $_ } | Where-Object { $_.Id -eq $script:PluginId }
    Check 'Plugin is installed and loaded' ($null -ne $plugin) "version=$($plugin.Version)"
    $names = (Get-PluginConfig).PSObject.Properties.Name
    Check 'Config has no removed features (global/exclusions/library/transcode)' (-not ($names -match 'Global|Exclu|Librar|Transcod|DirectPlay|PausedStream|PlaybackStart')) ''

    $probe = New-ProbeUser -Name 'we-regression' -DeviceId 'we-regression-1'
    $viaProbe = @{ 'X-Emby-Token' = $probe.Token }
    $config = Get-PluginConfig
    $config.EnableWatchTimeLimiter = $true
    $config.EnablePublicApi = $true
    $config.ApiKey = 'regression-test-key'
    $config | Add-Member -NotePropertyName LimitedUsers -NotePropertyValue @((New-LimitedUserEntry -User $probe -DailyMinutes 180)) -Force
    Save-PluginConfig $config
    $key = @{ 'X-WatchingEye-Api-Key' = 'regression-test-key' }

    Write-Host "== Authorization"
    foreach ($route in @(@('GET', '/WatchingEye/LimitedUsersStatus'), @('GET', '/WatchingEye/GetLogEvents'), @('POST', '/WatchingEye/ClearLogs'), @('POST', '/WatchingEye/ExtendTime'), @('POST', '/WatchingEye/TimeOutUser'))) {
        $code = Get-Status $route[0] $route[1] $viaProbe @{ UserId = $probe.Id; Minutes = 5 }
        Check "Non-admin is refused: $($route[0]) $($route[1])" ($code -eq 403) "HTTP $code"
    }
    Check 'Anonymous is refused (plugin config endpoint)' ((Get-Status 'GET' "/Plugins/$($script:PluginId)/Configuration" @{}) -eq 401)
    Check 'Non-admin cannot read plugin config (holds the API key)' ((Get-Status 'GET' "/Plugins/$($script:PluginId)/Configuration" $viaProbe) -eq 403)

    Write-Host "== Public API"
    $cfg = Get-PluginConfig; $cfg.EnablePublicApi = $false; Save-PluginConfig $cfg
    Check 'API disabled -> correct key is refused' ((Get-Status 'GET' '/WatchingEye/Public/Status' ($script:Admin + $key)) -eq 403)
    $cfg = Get-PluginConfig; $cfg.EnablePublicApi = $true; Save-PluginConfig $cfg
    Check 'API enabled -> correct key works' ((Get-Status 'GET' '/WatchingEye/Public/Status' ($script:Admin + $key)) -eq 200)
    Check 'Wrong key -> 403 (not 500)' ((Get-Status 'GET' '/WatchingEye/Public/Status' ($script:Admin + @{ 'X-WatchingEye-Api-Key' = 'wrong' })) -eq 403)
    Check 'Missing key -> 403' ((Get-Status 'GET' '/WatchingEye/Public/Status' $script:Admin) -eq 403)
    Check 'No Emby login at all -> 401' ((Get-Status 'GET' '/WatchingEye/Public/Status' $key) -eq 401)

    Write-Host "== Input validation"
    Check 'ExtendTime above the cap is rejected' ((Get-Status 'POST' '/WatchingEye/ExtendTime' $script:Admin @{ UserId = $probe.Id; Minutes = 43201 }) -eq 400)
    Check 'ExtendTime int.MaxValue is rejected' ((Get-Status 'POST' '/WatchingEye/ExtendTime' $script:Admin @{ UserId = $probe.Id; Minutes = 2147483647 }) -eq 400)
    Check 'ExtendTime zero/negative is rejected' ((Get-Status 'POST' '/WatchingEye/ExtendTime' $script:Admin @{ UserId = $probe.Id; Minutes = -5 }) -eq 400)
    Check 'Unknown user id is rejected' ((Get-Status 'POST' '/WatchingEye/ExtendTime' $script:Admin @{ UserId = [guid]::NewGuid().ToString(); Minutes = 5 }) -eq 400)
    Check 'Oversized user id is rejected' ((Get-Status 'POST' '/WatchingEye/ExtendTime' $script:Admin @{ UserId = ('x' * 100000); Minutes = 5 }) -eq 400)
    Check 'Unknown period is rejected' ((Get-Status 'POST' '/WatchingEye/ExtendPeriodTime' $script:Admin @{ UserId = $probe.Id; Period = 'fortnightly'; Minutes = 5 }) -eq 400)
    for ($i = 0; $i -lt 12; $i++) { Get-Status 'POST' '/WatchingEye/ExtendTime' $script:Admin @{ UserId = $probe.Id; Minutes = 43200 } | Out-Null }
    $credit = (Get-LimitedStatus $probe).CreditSecondsDaily
    Check 'Repeated extends never overflow (credit stays positive and capped)' (($credit -gt 0) -and ($credit -le 315360000)) "credit=$credit s"
    Get-Status 'POST' '/WatchingEye/ResetUserTime' $script:Admin @{ UserId = $probe.Id } | Out-Null
    $code = Get-Status 'POST' '/WatchingEye/TimeOutUser' $script:Admin @{ UserId = $probe.Id.ToUpper(); Minutes = 30 }
    $timedOut = (Get-LimitedStatus $probe).TimeOutUntil
    Check 'Time-out works with a different-case user id' (($code -eq 204) -and (([datetimeoffset]$timedOut).UtcDateTime -gt [datetime]::UtcNow)) "HTTP $code until=$timedOut"
    Get-Status 'POST' '/WatchingEye/ClearTimeOut' $script:Admin @{ UserId = $probe.Id } | Out-Null
    Check 'Time-out refuses a user who is not in Limited Users' ((Get-Status 'POST' '/WatchingEye/TimeOutUser' $script:Admin @{ UserId = 'someone-else'; Minutes = 5 }) -eq 400)

    Write-Host "== Configuration clamping"
    $cfg = Get-PluginConfig
    $cfg.LimitedUsers[0].ResetTimeOfDayHours = 100000000
    $cfg.LimitedUsers[0].DailyLimitMinutes = 99999
    Save-PluginConfig $cfg
    $saved = (Get-PluginConfig).LimitedUsers[0]
    Check 'Out-of-range reset hour and limit are clamped' (($saved.ResetTimeOfDayHours -le 23.99) -and ($saved.DailyLimitMinutes -le 1440)) "hour=$($saved.ResetTimeOfDayHours) minutes=$($saved.DailyLimitMinutes)"
    $bad = Get-PluginConfig
    $bad.LimitedUsers[0].EnableDailyLimit = $true
    $bad.LimitedUsers[0].DailyLimitMinutes = 0
    $badCode = Get-Status 'POST' "/Plugins/$($script:PluginId)/Configuration" $script:Admin $bad
    Check 'A config with an enabled limit of 0 is refused' (($badCode -ge 400) -and ($badCode -lt 500)) "HTTP $badCode"
    Check 'The refused config was not saved' ((Get-PluginConfig).LimitedUsers[0].DailyLimitMinutes -ge 1)

    if (-not $SkipEnforcement) {
        Write-Host "== Status contract for client apps (about 70 seconds)"
        $cfg = Get-PluginConfig
        $cfg.LimitedUsers[0].EnableDailyLimit = $true; $cfg.LimitedUsers[0].DailyLimitMinutes = 60
        $cfg.LimitedUsers[0].EnablePerDayLimits = $false
        Save-PluginConfig $cfg
        $row = Get-LimitedStatus $probe
        $expectedFields = @('UserId', 'Username', 'DailyLimitMinutes', 'WeeklyLimitHours', 'MonthlyLimitHours', 'YearlyLimitHours', 'SecondsWatchedDaily', 'SecondsWatchedWeekly', 'SecondsWatchedMonthly', 'SecondsWatchedYearly', 'CreditSecondsDaily', 'CreditSecondsWeekly', 'CreditSecondsMonthly', 'CreditSecondsYearly', 'TimeOutUntil', 'EffectiveDailyLimitMinutes', 'IsEnabled')
        $missing = @($expectedFields | Where-Object { $row.PSObject.Properties.Name -notcontains $_ })
        Check 'Status contains every field the Android app reads plus the new ones' ($missing.Count -eq 0) "missing=[$($missing -join ',')]"
        Check 'Effective daily limit equals the daily limit' ($row.EffectiveDailyLimitMinutes -eq 60) "value=$($row.EffectiveDailyLimitMinutes)"
        $cfg = Get-PluginConfig
        $today = (Get-Date).DayOfWeek.ToString()
        $cfg.LimitedUsers[0].EnablePerDayLimits = $true
        $cfg.LimitedUsers[0].PerDayLimits = @([pscustomobject]@{ Day = $today; IsEnabled = $true; LimitMinutes = 30 })
        Save-PluginConfig $cfg
        Check 'Effective daily limit honours today''s per-day override' ((Get-LimitedStatus $probe).EffectiveDailyLimitMinutes -eq 30) "value=$((Get-LimitedStatus $probe).EffectiveDailyLimitMinutes)"
        $cfg = Get-PluginConfig; $cfg.LimitedUsers[0].EnablePerDayLimits = $false; $cfg.LimitedUsers[0].PerDayLimits = @(); Save-PluginConfig $cfg

        $probe2 = New-ProbeUser -Name "O'Brien & Co" -DeviceId 'we-regression-3'
        $cfg = Get-PluginConfig
        $cfg | Add-Member -NotePropertyName LimitedUsers -NotePropertyValue @($cfg.LimitedUsers[0], (New-LimitedUserEntry -User $probe2 -DailyMinutes 60)) -Force
        Save-PluginConfig $cfg
        Check 'User names with quotes and ampersands are kept exactly' ((Get-LimitedStatus $probe2).Username -eq "O'Brien & Co") "reported=[$((Get-LimitedStatus $probe2).Username)]"

        Get-Status 'POST' '/WatchingEye/TimeOutUser' $script:Admin @{ UserId = $probe.Id; Minutes = 1 } | Out-Null
        Check 'An active time-out is reported' (([datetimeoffset](Get-LimitedStatus $probe).TimeOutUntil).UtcDateTime -gt [datetime]::UtcNow)
        Start-Sleep 65
        $expired = (Get-LimitedStatus $probe).TimeOutUntil
        Check 'An expired time-out is reported as none (0001-01-01)' (([datetime]$expired).Year -eq 1) "TimeOutUntil=$expired"
    }

    if (-not $SkipEnforcement) {
        Write-Host "== Enforcement (about 90 seconds)"
        $cfg = Get-PluginConfig; $cfg.LimitedUsers[0].ResetTimeOfDayHours = 3.0; $cfg.LimitedUsers[0].DailyLimitMinutes = 1
        $cfg.TimeOutMessageText = 'Test time-out. Playback is disabled until {end_time}.'; Save-PluginConfig $cfg
        Get-Status 'POST' '/WatchingEye/ResetUserTime' $script:Admin @{ UserId = $probe.Id } | Out-Null
        Invoke-RestMethod "$($script:Api)/Sessions/Capabilities/Full?DeviceId=$($probe.DeviceId)" -Method Post -Headers $viaProbe -ContentType 'application/json' -Body (@{ PlayableMediaTypes = @('Video', 'Audio'); SupportedCommands = @('DisplayMessage'); SupportsMediaControl = $true } | ConvertTo-Json) | Out-Null
        $listener = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $PSScriptRoot 'ws-listen.ps1')`" -Token $($probe.Token) -DeviceId $($probe.DeviceId) -Out `"$logFile`""
        Start-Sleep 4
        Send-PlaybackReport $probe '/Sessions/Playing' 'regression-play' 0
        for ($i = 1; $i -le 16; $i++) { Start-Sleep 5; Send-PlaybackReport $probe '/Sessions/Playing/Progress' 'regression-play' ([int64]($i * 50000000)) }
        Send-PlaybackReport $probe '/Sessions/Playing/Stopped' 'regression-play' 1
        $watched = [int](Get-LimitedStatus $probe).SecondsWatchedDaily
        $messages = Get-Content -LiteralPath $logFile -ErrorAction SilentlyContinue
        Check 'Watch time is counted' ($watched -ge 60) "watched=$watched s (limit 60 s)"
        Check 'A Stop command was sent at the limit' (@($messages | Where-Object { $_ -match '"Command":"Stop"' }).Count -ge 1)
        Check 'The limit message was sent' (@($messages | Where-Object { $_ -match 'Time Limit Reached' }).Count -ge 1)
        $events = @(Invoke-RestMethod "$($script:Api)/WatchingEye/GetLogEvents" -Headers $script:Admin)
        Check 'A LimitReached event was logged' (@($events | Where-Object { $_.EventType -eq 'LimitReached' -and $_.Username -eq $probe.Name }).Count -ge 1)
        $mark = @($messages).Count
        Send-PlaybackReport $probe '/Sessions/Playing' 'regression-blocked' 0; Start-Sleep 4
        $after = @(Get-Content -LiteralPath $logFile | Select-Object -Skip $mark)
        Check 'Starting playback while over the limit is blocked' (@($after | Where-Object { $_ -match '"Command":"Stop"' }).Count -ge 1)
        Send-PlaybackReport $probe '/Sessions/Playing/Stopped' 'regression-blocked' 1
        Get-Status 'POST' '/WatchingEye/ResetUserTime' $script:Admin @{ UserId = $probe.Id } | Out-Null
        $mark = @(Get-Content -LiteralPath $logFile).Count
        Get-Status 'POST' '/WatchingEye/TimeOutUser' $script:Admin @{ UserId = $probe.Id; Minutes = 30 } | Out-Null; Start-Sleep 3
        $timeoutMessage = @(Get-Content -LiteralPath $logFile | Select-Object -Skip $mark | Where-Object { $_ -match 'Playback Disabled' })
        $expected = (Get-Date).AddMinutes(30).ToString('h:mm tt')
        Check 'Time-out message states the local end time' (($timeoutMessage.Count -ge 1) -and ($timeoutMessage[0] -match [regex]::Escape(' at ')) -and ($timeoutMessage[0] -match 'until')) "expected about $expected"
        Get-Status 'POST' '/WatchingEye/ClearTimeOut' $script:Admin @{ UserId = $probe.Id } | Out-Null

        Write-Host "== Name sanitising (client-supplied markup must not reach the log)"
        Invoke-RestMethod "$($script:Api)/WatchingEye/ClearLogs" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body '{}' | Out-Null
        Get-Status 'POST' '/WatchingEye/ResetUserTime' $script:Admin @{ UserId = $probe.Id } | Out-Null
        $marker = '<b>marker</b>'
        $query = "X-Emby-Client=$marker&X-Emby-Device-Id=we-regression-2&X-Emby-Device-Name=test&X-Emby-Client-Version=1&X-Emby-Token=$($probe.Token)"
        for ($i = 0; $i -le 16; $i++) {
            $path = if ($i -eq 0) { '/Sessions/Playing' } else { '/Sessions/Playing/Progress' }
            $body = @{ ItemId = '15'; PlaySessionId = 'regression-name'; PositionTicks = [int64]($i * 50000000); IsPaused = $false; PlayMethod = 'DirectPlay' } | ConvertTo-Json
            Invoke-RestMethod "$($script:Api)$path`?$query" -Method Post -ContentType 'application/json' -Body $body | Out-Null
            if ($i -gt 0) { Start-Sleep 5 }
        }
        $names = @(Invoke-RestMethod "$($script:Api)/WatchingEye/GetLogEvents" -Headers $script:Admin) | ForEach-Object { $_.ClientName }
        Check 'Logged client names contain no angle brackets' ((@($names).Count -ge 1) -and (-not (($names -join ' ') -match '[<>]'))) "names=[$($names -join ' | ')]"
    }

    if (-not $SkipLockout) {
        Write-Host "== Public API lockout"
        for ($i = 1; $i -le 6; $i++) { Get-Status 'GET' '/WatchingEye/Public/Status' ($script:Admin + @{ 'X-WatchingEye-Api-Key' = "wrong$i" }) | Out-Null }
        Check 'After repeated failures even the correct key is refused (lockout)' ((Get-Status 'GET' '/WatchingEye/Public/Status' ($script:Admin + $key)) -eq 403) '(lockout lasts 5 minutes or until Emby restarts)'
    }
}
finally {
    Write-Host "== Cleanup"
    if ($listener) { Stop-Process -Id $listener.Id -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }
    try { if ($probe) { Invoke-RestMethod "$($script:Api)/WatchingEye/DeleteUserData" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body (@{ UserId = $probe.Id } | ConvertTo-Json) | Out-Null } } catch { }
    try { Reset-PluginState } catch { Write-Host "Could not reset plugin state: $($_.Exception.Message)" }
    foreach ($extra in @($probe2)) { if ($extra) { try { Invoke-RestMethod "$($script:Api)/WatchingEye/DeleteUserData" -Method Post -Headers $script:Admin -ContentType "application/json" -Body (@{ UserId = $extra.Id } | ConvertTo-Json) | Out-Null } catch { } } }
    Remove-ProbeUser $probe2
    Remove-ProbeUser $probe
}

$failed = @($results | Where-Object { -not $_.Passed })
Write-Host ""
Write-Host ("{0} checks, {1} passed, {2} failed" -f $results.Count, ($results.Count - $failed.Count), $failed.Count)
if ($failed.Count -gt 0) { exit 1 }
