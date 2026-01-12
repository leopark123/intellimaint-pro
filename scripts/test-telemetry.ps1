# Check current telemetry values

$loginBody = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResult.data.token
$headers = @{ Authorization = "Bearer $token" }

Write-Host "=== Current Telemetry for Motor Tags ===" -ForegroundColor Cyan

$tags = @("Motor1_Speed", "Motor1_Current", "Motor1_Temp", "Motor1_Running")

foreach ($tag in $tags) {
    Write-Host ""
    Write-Host "Tag: $tag" -ForegroundColor Green
    try {
        $data = Invoke-RestMethod -Uri "http://localhost:5000/api/telemetry/latest?deviceId=SIM-PLC-001&tagId=$tag" -Headers $headers
        $data.data | ConvertTo-Json -Depth 3
    } catch {
        Write-Host "Error: $_"
    }
}

Write-Host ""
Write-Host "=== Operation Mode Trigger Conditions ===" -ForegroundColor Cyan
Write-Host "- Normal Operation: Motor1_Speed 30-60"
Write-Host "- Low Speed: Motor1_Speed 10-30"
Write-Host "- Idle/Standby: Motor1_Speed 0-10"
