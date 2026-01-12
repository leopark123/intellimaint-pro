# Fix operation mode min duration

$loginBody = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResult.data.token
$headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }

$instances = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances" -Headers $headers).data

foreach ($inst in $instances) {
    Write-Host "=== Fixing modes for: $($inst.instanceId) ($($inst.name)) ===" -ForegroundColor Cyan

    $modes = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$($inst.instanceId)/modes" -Headers $headers).data

    foreach ($mode in $modes) {
        if ($mode.minDurationMs -gt 1000) {
            Write-Host "  Updating '$($mode.name)' minDurationMs from $($mode.minDurationMs) to 0"

            $updateBody = @{
                name = $mode.name
                description = $mode.description
                triggerTagId = $mode.triggerTagId
                triggerMinValue = $mode.triggerMinValue
                triggerMaxValue = $mode.triggerMaxValue
                minDurationMs = 0  # Changed to 0 for instant detection
                maxDurationMs = $mode.maxDurationMs
                priority = $mode.priority
                enabled = $mode.enabled
            } | ConvertTo-Json

            try {
                Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$($inst.instanceId)/modes/$($mode.modeId)" -Method Put -Headers $headers -Body $updateBody | Out-Null
                Write-Host "    Done" -ForegroundColor Green
            } catch {
                Write-Host "    Error: $_" -ForegroundColor Red
            }
        }
    }
}

Write-Host ""
Write-Host "Done!"
