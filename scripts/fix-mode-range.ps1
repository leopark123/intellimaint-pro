# Fix operation mode trigger range

$loginBody = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResult.data.token
$headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/json" }

# Get all instances
$instances = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances" -Headers $headers).data

foreach ($inst in $instances) {
    Write-Host "=== Fixing modes for: $($inst.instanceId) ($($inst.name)) ===" -ForegroundColor Cyan

    # Get operation modes
    $modes = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$($inst.instanceId)/modes" -Headers $headers).data

    foreach ($mode in $modes) {
        if ($mode.name -eq "Normal Operation" -and $mode.triggerMaxValue -lt 100) {
            Write-Host "  Updating '$($mode.name)' max value from $($mode.triggerMaxValue) to 100"

            $updateBody = @{
                name = $mode.name
                description = $mode.description
                triggerTagId = $mode.triggerTagId
                triggerMinValue = $mode.triggerMinValue
                triggerMaxValue = 100  # Changed from 60 to 100
                minDurationMs = $mode.minDurationMs
                maxDurationMs = $mode.maxDurationMs
                priority = $mode.priority
                enabled = $mode.enabled
            } | ConvertTo-Json

            try {
                $result = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$($inst.instanceId)/modes/$($mode.modeId)" -Method Put -Headers $headers -Body $updateBody
                Write-Host "    Updated successfully" -ForegroundColor Green
            } catch {
                Write-Host "    Error: $_" -ForegroundColor Red
            }
        }
    }
}

Write-Host ""
Write-Host "Done! Wait a few seconds for mode detection to update..."
