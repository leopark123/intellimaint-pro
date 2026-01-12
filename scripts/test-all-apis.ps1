# Complete API Test Script
$baseUrl = "http://localhost:5000"

Write-Host "=== IntelliMaint API Test Suite ===" -ForegroundColor Cyan
Write-Host ""

# 1. Get Token
Write-Host "1. Login and get token..." -ForegroundColor Yellow
$loginResult = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method POST -ContentType 'application/json' -Body '{"username":"admin","password":"admin123"}'
$token = $loginResult.data.token
Write-Host "   Token: $($token.Substring(0, 30))..." -ForegroundColor Green
$headers = @{Authorization="Bearer $token"}

# 2. Test Devices API
Write-Host "`n2. Devices API..." -ForegroundColor Yellow
$devices = Invoke-RestMethod -Uri "$baseUrl/api/devices" -Headers $headers
Write-Host "   Found $($devices.data.Count) devices" -ForegroundColor Green
$devices.data | ForEach-Object { Write-Host "   - $($_.deviceId): $($_.name) [Protocol: $($_.protocol)]" }

# 3. Test Tags API
Write-Host "`n3. Tags API..." -ForegroundColor Yellow
$tags = Invoke-RestMethod -Uri "$baseUrl/api/telemetry/tags" -Headers $headers
Write-Host "   Found $($tags.data.Count) tags" -ForegroundColor Green
$tags.data | ForEach-Object { Write-Host "   - $($_.deviceId)/$($_.tagId): $($_.valueType)" }

# 4. Test Telemetry Query API
Write-Host "`n4. Telemetry Query API..." -ForegroundColor Yellow
$telemetry = Invoke-RestMethod -Uri "$baseUrl/api/telemetry/query?deviceId=SIM-PLC-001&tagId=Motor1_Speed&limit=3" -Headers $headers
Write-Host "   Found $($telemetry.data.Count) records (hasMore: $($telemetry.hasMore))" -ForegroundColor Green

# 5. Test Latest Telemetry API
Write-Host "`n5. Latest Telemetry API..." -ForegroundColor Yellow
$latest = Invoke-RestMethod -Uri "$baseUrl/api/telemetry/latest?deviceId=SIM-PLC-001" -Headers $headers
Write-Host "   Found $($latest.data.Count) latest values" -ForegroundColor Green
$latest.data | ForEach-Object { Write-Host "   - $($_.tagId): $($_.value)" }

# 6. Test Alarms API
Write-Host "`n6. Alarms API..." -ForegroundColor Yellow
$alarms = Invoke-RestMethod -Uri "$baseUrl/api/alarms?limit=5" -Headers $headers
Write-Host "   Found $($alarms.data.items.Count) alarms (total: $($alarms.data.totalCount))" -ForegroundColor Green

# 7. Test Alarm Rules API
Write-Host "`n7. Alarm Rules API..." -ForegroundColor Yellow
$rules = Invoke-RestMethod -Uri "$baseUrl/api/alarm-rules" -Headers $headers
Write-Host "   Found $($rules.data.Count) alarm rules" -ForegroundColor Green

# 8. Test Health Assessment API
Write-Host "`n8. Health Assessment API..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$baseUrl/api/health-assessment/devices" -Headers $headers
    Write-Host "   Found $($health.data.Count) device health scores" -ForegroundColor Green
} catch {
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== All API Tests Completed ===" -ForegroundColor Cyan
