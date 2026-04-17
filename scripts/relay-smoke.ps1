param([int]$Port = 3099)

# 1. /healthz
$health = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/healthz" -UseBasicParsing
Write-Host "healthz: $($health.StatusCode) $($health.Content)"

# 2. WebSocket Hello / HelloAck
Add-Type -AssemblyName System.Net.WebSockets

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(5000)

$uri = New-Object System.Uri("ws://127.0.0.1:$Port/blocker/ws-relay")
$ws.ConnectAsync($uri, $cts.Token).Wait()
Write-Host "ws state after connect: $($ws.State)"

# Build Hello: [0x01][proto:1=1][sim:uint16 LE=1,0][nameLen:varint=4]['j','j','a','c']
$name = [System.Text.Encoding]::UTF8.GetBytes("jjac")
$msg = New-Object byte[] (1 + 1 + 2 + 1 + $name.Length)
$msg[0] = 0x01      # Hello
$msg[1] = 1         # proto version
$msg[2] = 1         # sim version low
$msg[3] = 0         # sim version high
$msg[4] = $name.Length
[Array]::Copy($name, 0, $msg, 5, $name.Length)

$seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$msg)
$ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, $cts.Token).Wait()
Write-Host "sent hello ($($msg.Length) bytes)"

# Receive HelloAck
$rxBuf = New-Object byte[] 256
$rxSeg = New-Object System.ArraySegment[byte] -ArgumentList @(,$rxBuf)
$result = $ws.ReceiveAsync($rxSeg, $cts.Token).Result
$received = $rxBuf[0..($result.Count - 1)]
Write-Host "received $($result.Count) bytes: $([System.BitConverter]::ToString($received))"

if ($received[0] -eq 0x02 -and $received[1] -eq 1) {
    Write-Host "OK: HelloAck received with proto=1"
    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $cts.Token).Wait()
    exit 0
} else {
    Write-Host "FAIL: unexpected response"
    exit 1
}
