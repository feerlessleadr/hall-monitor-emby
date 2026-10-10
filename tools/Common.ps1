# Shared helpers for the Watching Eye test scripts. Dot-source this file; do not run it directly.
#
# Nothing in this repository contains credentials: the Emby admin API key is read from a file you
# point to (-KeyFile). Temporary test users get a random password that is never written to disk or printed.

$script:PluginId = 'e8c3b1b3-4f56-4f38-a28a-2e6c5a043007'

function Initialize-Context {
    param(
        [string]$BaseUrl = 'http://localhost:8096',
        [string]$KeyFile = (Join-Path $env:USERPROFILE '.emby-spike-key')
    )
    if (-not (Test-Path -LiteralPath $KeyFile)) { throw "Admin API key file not found: $KeyFile" }
    $script:Api = "$BaseUrl/emby"
    $script:Admin = @{ 'X-Emby-Token' = (Get-Content -LiteralPath $KeyFile -Raw).Trim() }
    $script:ConfigUrl = "$($script:Api)/Plugins/$($script:PluginId)/Configuration"
}

# Returns the HTTP status code only (never throws on 4xx/5xx).
function Get-Status {
    param([string]$Method, [string]$Path, [hashtable]$Headers, $Body = $null)
    try {
        $request = @{ Uri = "$($script:Api)$Path"; Method = $Method; Headers = $Headers; UseBasicParsing = $true; TimeoutSec = 120 }
        if ($null -ne $Body -and $Method -ne 'GET') { $request.ContentType = 'application/json'; $request.Body = ($Body | ConvertTo-Json -Depth 8) }
        return [int](Invoke-WebRequest @request).StatusCode
    } catch {
        return [int]$_.Exception.Response.StatusCode.value__
    }
}

function Get-PluginConfig { Invoke-RestMethod $script:ConfigUrl -Headers $script:Admin }

function Save-PluginConfig {
    param($Config)
    Invoke-RestMethod $script:ConfigUrl -Method Post -Headers $script:Admin -ContentType 'application/json' -Body ($Config | ConvertTo-Json -Depth 8) | Out-Null
}

# Creates a throw-away non-admin user and returns @{ Id; Token; Name }.
function New-ProbeUser {
    param([string]$Name = 'we-test', [string]$DeviceId = 'we-test-device')
    $password = [guid]::NewGuid().ToString('N')
    $user = Invoke-RestMethod "$($script:Api)/Users/New" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body (@{ Name = $Name } | ConvertTo-Json)
    Invoke-RestMethod "$($script:Api)/Users/$($user.Id)/Password" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body (@{ NewPw = $password } | ConvertTo-Json) | Out-Null
    $auth = "MediaBrowser Client=`"we-test`", Device=`"test`", DeviceId=`"$DeviceId`", Version=`"1`""
    $login = Invoke-RestMethod "$($script:Api)/Users/AuthenticateByName" -Method Post -ContentType 'application/json' -Headers @{ 'X-Emby-Authorization' = $auth } -Body (@{ Username = $Name; Pw = $password } | ConvertTo-Json)
    return @{ Id = $user.Id; Token = $login.AccessToken; Name = $Name; DeviceId = $DeviceId }
}

function Remove-ProbeUser {
    param($User)
    if ($User) { try { Invoke-RestMethod "$($script:Api)/Users/$($User.Id)" -Method Delete -Headers $script:Admin | Out-Null } catch { } }
}

# A minimal limited-user entry for the plugin configuration.
function New-LimitedUserEntry {
    param($User, [int]$DailyMinutes = 180)
    return [pscustomobject]@{
        UserId = $User.Id; Username = $User.Name; IsEnabled = $true
        EnableDailyLimit = $true; DailyLimitMinutes = $DailyMinutes
        EnableWeeklyLimit = $false; EnableMonthlyLimit = $false; EnableYearlyLimit = $false
        EnableDailyReset = $true; ResetTimeOfDayHours = 3.0
        EnableThresholdNotifications = $false; NotificationThresholds = '80,95'
        EnableTimeWindows = $false; TimeWindows = @(); EnablePerDayLimits = $false; PerDayLimits = @()
    }
}

# Restores the plugin to a clean, disabled state and clears its log.
function Reset-PluginState {
    $config = Get-PluginConfig
    $config.EnableWatchTimeLimiter = $false
    $config.EnablePublicApi = $false
    $config.ApiKey = ''
    $config | Add-Member -NotePropertyName LimitedUsers -NotePropertyValue @() -Force
    Save-PluginConfig $config
    Invoke-RestMethod "$($script:Api)/WatchingEye/ClearLogs" -Method Post -Headers $script:Admin -ContentType 'application/json' -Body '{}' | Out-Null
}

# Simulates a client reporting playback of an item (the plugin counts time from these reports).
function Send-PlaybackReport {
    param($User, [string]$Path, [string]$PlaySessionId, [int64]$PositionTicks = 0, [string]$ItemId = '15')
    $body = @{ ItemId = $ItemId; PlaySessionId = $PlaySessionId; PositionTicks = $PositionTicks; IsPaused = $false; CanSeek = $true; PlayMethod = 'DirectPlay' } | ConvertTo-Json
    Invoke-RestMethod "$($script:Api)$Path" -Method Post -Headers @{ 'X-Emby-Token' = $User.Token } -ContentType 'application/json' -Body $body | Out-Null
}

# Starts tools\ws-push-client.ps1 (the app's live connection) hidden and returns the process; messages go to $LogFile.
function Start-PushClient {
    param([string]$Token, [string]$LogFile, [string]$PluginKey = "", [int]$Starts = 1, [switch]$Sessions)
    $script = Join-Path $PSScriptRoot 'ws-push-client.ps1'
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Token $Token -Out `"$LogFile`" -PluginKey `"$PluginKey`" -Starts $Starts"
    if ($Sessions) { $arguments += ' -Sessions' }
    return Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList $arguments
}

# Returns the parsed Watching Eye status pushes (oldest first) from a push client's log file.
function Get-StatusPushes {
    param([string]$LogFile)
    $lines = @(Get-Content -LiteralPath $LogFile -ErrorAction SilentlyContinue | Where-Object { $_ -match '"MessageType":"WatchingEyeStatus"' })
    return @($lines | ForEach-Object { ($_ -replace '^\S+ ', '') | ConvertFrom-Json } | ForEach-Object { $_.Data })
}
