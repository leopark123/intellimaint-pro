# First restore from git
Set-Location "E:/DAYDAYUP/intellimaint-pro-v56"
git checkout -- src/Host.Api/Program.cs
Start-Sleep -Milliseconds 500

$file = "E:/DAYDAYUP/intellimaint-pro-v56/src/Host.Api/Program.cs"
$content = Get-Content $file -Raw

$old = 'builder.Services.AddSignalR();'
$new = @'
builder.Services.AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        options.EnableDetailedErrors = true;
    });
'@

$content = $content -replace [regex]::Escape($old), $new
[System.IO.File]::WriteAllText($file, $content)

Write-Host "SignalR server configuration updated"
