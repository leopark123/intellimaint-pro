# Test Motor Instance Details

$loginBody = @{ username = "admin"; password = "admin123" } | ConvertTo-Json
$loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
$token = $loginResult.data.token
$headers = @{ Authorization = "Bearer $token" }

# Get first instance detail
$instances = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances" -Headers $headers
$instanceId = $instances[0].instanceId
Write-Host "=== Instance: $instanceId ===" -ForegroundColor Cyan

# Get instance detail
Write-Host ""
Write-Host "=== 1. Instance Detail ===" -ForegroundColor Green
$detail = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$instanceId/detail" -Headers $headers
$detail | ConvertTo-Json -Depth 5

# Get parameter mappings
Write-Host ""
Write-Host "=== 2. Parameter Mappings ===" -ForegroundColor Green
$mappings = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$instanceId/mappings" -Headers $headers
Write-Host "Mappings count: $($mappings.Count)"
$mappings | ConvertTo-Json -Depth 3

# Get operation modes
Write-Host ""
Write-Host "=== 3. Operation Modes ===" -ForegroundColor Green
$modes = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$instanceId/modes" -Headers $headers
Write-Host "Modes count: $($modes.Count)"
$modes | ConvertTo-Json -Depth 3

# Get baselines
Write-Host ""
Write-Host "=== 4. Baselines ===" -ForegroundColor Green
$baselines = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$instanceId/baselines" -Headers $headers
Write-Host "Baselines count: $($baselines.Count)"
$baselines | ConvertTo-Json -Depth 3

# Try to trigger diagnosis
Write-Host ""
Write-Host "=== 5. Try to Diagnose ===" -ForegroundColor Green
try {
    $diagResult = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances/$instanceId/diagnose" -Method Post -Headers $headers -ContentType "application/json" -Body "{}"
    $diagResult | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Diagnosis error: $($_.Exception.Message)"
    $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
    $reader.BaseStream.Position = 0
    $reader.DiscardBufferedData()
    $responseBody = $reader.ReadToEnd()
    Write-Host "Response: $responseBody"
}
