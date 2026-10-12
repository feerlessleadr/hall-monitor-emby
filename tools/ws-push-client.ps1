# Acts as the Android app's live connection: connects to the Emby websocket (token sent as a header, never in the
# URL), subscribes to the Hall Monitor status push (and optionally to Emby's session push), and appends every message
# it receives to a log file. Used by the regression suite; run until the process is stopped.
#
#   powershell -ExecutionPolicy Bypass -File tools\ws-push-client.ps1 -Token <Emby token> -Out <file> [-PluginKey <key>] [-Sessions]
#
# Only use it with a test server and throw-away tokens.
param(
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$Out,
    [string]$PluginKey = '',
    [int]$Starts = 1,
    [switch]$Sessions,
    [string]$DeviceId = 'we-push-client',
    [string]$BaseHost = 'localhost:8096'
)

$socket = New-Object System.Net.WebSockets.ClientWebSocket
$socket.Options.SetRequestHeader('X-Emby-Token', $Token)
$socket.ConnectAsync([uri]"ws://$BaseHost/embywebsocket?deviceId=$DeviceId", [Threading.CancellationToken]::None).Wait()
"CONNECTED $(Get-Date -Format o)" | Out-File -LiteralPath $Out -Encoding utf8

function Send-Text([string]$Text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $socket.SendAsync([ArraySegment[byte]]$bytes, 'Text', $true, [Threading.CancellationToken]::None).Wait()
}
for ($startIndex = 0; $startIndex -lt $Starts; $startIndex++) { Send-Text ('{"MessageType":"HallMonitorStatusStart","Data":"0,30000,' + $PluginKey + '"}') }
if ($Sessions) { Send-Text '{"MessageType":"SessionsStart","Data":"0,1500"}' }

$buffer = New-Object byte[] 1048576
while ($socket.State -eq 'Open') {
    $receive = $socket.ReceiveAsync([ArraySegment[byte]]$buffer, [Threading.CancellationToken]::None)
    $receive.Wait()
    if ($receive.Result.MessageType -eq 'Close') { break }
    $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $receive.Result.Count)
    "$(Get-Date -Format o) $text" | Out-File -LiteralPath $Out -Append -Encoding utf8
}
"CLOSED $(Get-Date -Format o)" | Out-File -LiteralPath $Out -Append -Encoding utf8
