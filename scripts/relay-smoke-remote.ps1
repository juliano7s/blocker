param([string]$Uri = "wss://julianoschroeder.com/blocker/ws-relay")

$ws = New-Object System.Net.WebSockets.ClientWebSocket
$cts = New-Object System.Threading.CancellationTokenSource
$cts.CancelAfter(10000)

$u = New-Object System.Uri($Uri)
$ws.ConnectAsync($u, $cts.Token).Wait()
Write-Host "ws state after connect: $($ws.State)"

$name = [System.Text.Encoding]::UTF8.GetBytes("jjac")
$msg = New-Object byte[] (1 + 1 + 2 + 1 + $name.Length)
$msg[0] = 0x01
$msg[1] = 1
$msg[2] = 1
$msg[3] = 0
$msg[4] = $name.Length
[Array]::Copy($name, 0, $msg, 5, $name.Length)

$seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$msg)
$ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, $cts.Token).Wait()
Write-Host "sent hello ($($msg.Length) bytes)"

$rxBuf = New-Object byte[] 256
$rxSeg = New-Object System.ArraySegment[byte] -ArgumentList @(,$rxBuf)
$result = $ws.ReceiveAsync($rxSeg, $cts.Token).Result
$received = $rxBuf[0..($result.Count - 1)]
Write-Host "received $($result.Count) bytes: $([System.BitConverter]::ToString($received))"

if ($received[0] -eq 0x02 -and $received[1] -eq 1) {
    Write-Host "OK: HelloAck from production relay"
    $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $cts.Token).Wait()
    exit 0
} else {
    Write-Host "FAIL: unexpected response"
    exit 1
}
