# Acts as an Emby client device: connects to the server websocket and appends every message it receives
# to a log file. Used to observe the commands the plugin sends (Stop, DisplayMessage) without a real player.
#
#   powershell -ExecutionPolicy Bypass -File tools\ws-listen.ps1 -Token <user token> -DeviceId <id> -Out <file>
#
# Runs until the process is stopped (Stop-Process). The user token is passed on the command line, so only use
# it with throw-away test users.
param(
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$DeviceId,
    [Parameter(Mandatory)][string]$Out,
    [string]$BaseHost = 'localhost:8096'
)

$socket = New-Object System.Net.WebSockets.ClientWebSocket
$socket.ConnectAsync([uri]"ws://$BaseHost/embywebsocket?api_key=$Token&deviceId=$DeviceId", [Threading.CancellationToken]::None).Wait()
"CONNECTED $(Get-Date -Format o)" | Out-File -LiteralPath $Out -Encoding utf8
$buffer = New-Object byte[] 65536
# Do not use a cancellable receive: cancelling a receive aborts a .NET websocket.
while ($socket.State -eq 'Open') {
    $receive = $socket.ReceiveAsync([ArraySegment[byte]]$buffer, [Threading.CancellationToken]::None)
    $receive.Wait()
    if ($receive.Result.MessageType -eq 'Close') { break }
    $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $receive.Result.Count)
    "$(Get-Date -Format o) $text" | Out-File -LiteralPath $Out -Append -Encoding utf8
}
"CLOSED $(Get-Date -Format o)" | Out-File -LiteralPath $Out -Append -Encoding utf8
