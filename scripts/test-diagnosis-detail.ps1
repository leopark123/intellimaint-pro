# Test diagnosis detail with parameterName

$loginBody = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResult.data.token
$headers = @{ Authorization = "Bearer $token" }

$diagnoses = (Invoke-RestMethod -Uri "http://localhost:5000/api/motor-diagnoses" -Headers $headers).data

if ($diagnoses.Count -gt 0) {
    Write-Host "=== First Diagnosis ===" -ForegroundColor Cyan
    $d = $diagnoses[0]
    Write-Host "InstanceId: $($d.instanceId)"
    Write-Host "HealthScore: $($d.healthScore)"
    Write-Host ""
    Write-Host "=== Deviations ===" -ForegroundColor Green
    foreach ($dev in $d.deviations) {
        Write-Host "  Parameter: $($dev.parameter)"
        Write-Host "  ParameterName: $($dev.parameterName)"
        Write-Host "  CurrentValue: $($dev.currentValue)"
        Write-Host "  ---"
    }
} else {
    Write-Host "No diagnoses found"
}
