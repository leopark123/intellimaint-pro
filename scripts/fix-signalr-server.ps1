$file = "E:/DAYDAYUP/intellimaint-pro-v56/src/Host.Api/Program.cs"
$content = Get-Content $file -Raw

$oldPattern = @"
    // P2: SignalR
    builder.Services.AddSignalR();
"@

$newPattern = @"
    // P2: SignalR - 配置连接保活
    builder.Services.AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(15); // 15秒心跳
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // 60秒客户端超时
        options.HandshakeTimeout = TimeSpan.FromSeconds(15); // 15秒握手超时
        options.EnableDetailedErrors = true; // 开发环境启用详细错误
    });
"@

$content = $content -replace [regex]::Escape($oldPattern), $newPattern
Set-Content $file $content -NoNewline

Write-Host "Server SignalR configuration updated successfully"
