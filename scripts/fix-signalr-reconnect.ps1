$file = "E:/DAYDAYUP/intellimaint-pro-v56/intellimaint-ui/src/api/signalr.ts"
$content = Get-Content $file -Raw

$oldPattern = @"
    this.connection.onreconnected(() => {
      console.info('SignalR reconnected')
      this.emitConnection(true)
      // Re-subscribe after reconnect (hook will call subscribeAll)
    })
"@

$newPattern = @"
    this.connection.onreconnected(async () => {
      console.info('SignalR reconnected')
      this.emitConnection(true)
      // v56.2: 重连后自动恢复订阅
      if (this.currentSubscription) {
        const sub = this.currentSubscription
        this.currentSubscription = null // 重置以允许重新订阅
        if (sub.type === 'device' && sub.deviceId) {
          await this.subscribeDevice(sub.deviceId)
        } else {
          await this.subscribeAll()
        }
      }
    })
"@

$content = $content -replace [regex]::Escape($oldPattern), $newPattern
Set-Content $file $content -NoNewline

Write-Host "SignalR reconnect handler updated successfully"
