# Test Mode Detection

$loginBody = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResult.data.token
$headers = @{ Authorization = "Bearer $token" }

# Check current mode for each instance
$instances = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances" -Headers $headers).data

foreach ($inst in $instances) {
    Write-Host "=== Instance: $($inst.instanceId) ($($inst.name)) ===" -ForegroundColor Cyan
    Write-Host "DeviceId: $($inst.deviceId)"

    # Get parameter mappings
    $mappings = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$($inst.instanceId)/mappings" -Headers $headers).data
    Write-Host "Mappings:"
    foreach ($m in $mappings) {
        Write-Host "  - $($m.parameter): $($m.tagId)"
    }

    # Get current mode
    Write-Host ""
    Write-Host "Current Mode:"
    try {
        $modeResult = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$($inst.instanceId)/current-mode" -Headers $headers
        $modeResult | ConvertTo-Json -Depth 3
    } catch {
        Write-Host "Error: $_"
    }

    # Get latest telemetry for speed tag
    $speedMapping = $mappings | Where-Object { $_.parameter -eq 21 }  # Speed parameter
    if ($speedMapping) {
        Write-Host ""
        Write-Host "Latest Speed Value:"
        try {
            $telemetry = Invoke-RestMethod -Uri "http://localhost:5000/api/telemetry/latest?deviceId=$($inst.deviceId)&tagId=$($speedMapping.tagId)" -Headers $headers
            $telemetry.data | ConvertTo-Json -Depth 2
        } catch {
            Write-Host "Error: $_"
        }
    }

    Write-Host ""
}
