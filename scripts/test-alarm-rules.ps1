$baseUrl = "http://localhost:5000"
$loginResult = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method POST -ContentType 'application/json' -Body '{"username":"admin","password":"admin123"}'
$token = $loginResult.data.token
$headers = @{Authorization="Bearer $token"}

Write-Host "Testing Alarm Rules API..." -ForegroundColor Yellow
try {
    $rules = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Headers $headers
    Write-Host "Found $($rules.data.Count) alarm rules" -ForegroundColor Green
    $rules.data | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    $_.Exception.Response
}
