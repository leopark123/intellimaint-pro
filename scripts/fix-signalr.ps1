$file = "E:/DAYDAYUP/intellimaint-pro-v56/intellimaint-ui/src/api/signalr.ts"
$content = Get-Content $file -Raw

$oldPattern = @"
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Information)
      .build()
"@

$newPattern = @"
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 30000]) // 更快的重连间隔
      .withServerTimeout(60000) // 60秒服务器超时
      .withKeepAliveInterval(15000) // 15秒心跳保活
      .configureLogging(LogLevel.Warning) // 减少日志噪音
      .build()
"@

$content = $content -replace [regex]::Escape($oldPattern), $newPattern
Set-Content $file $content -NoNewline

Write-Host "SignalR configuration updated successfully"
