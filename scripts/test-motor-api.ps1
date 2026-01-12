# Test Motor API

$ErrorActionPreference = "Continue"

# Login
$loginBody = @{
    username = "admin"
    password = "admin123"
} | ConvertTo-Json

Write-Host "=== 1. Login ===" -ForegroundColor Cyan
try {
    $loginResult = Invoke-RestMethod -Uri "http://localhost:5000/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
    Write-Host "Login response:"
    $loginResult | ConvertTo-Json -Depth 3

    $token = $loginResult.data.token
    if ($token) {
        Write-Host "Token obtained successfully"
    } else {
        Write-Host "Token is null, login may have failed"
        exit 1
    }
} catch {
    Write-Host "Login error: $_"
    exit 1
}

$headers = @{
    Authorization = "Bearer $token"
}

Write-Host ""
Write-Host "=== 2. Get Motor Models ===" -ForegroundColor Cyan
try {
    $models = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-models" -Headers $headers
    Write-Host "Motor models count: $($models.Count)"
    $models | ConvertTo-Json -Depth 3
} catch {
    Write-Host "Error: $_"
}

Write-Host ""
Write-Host "=== 3. Get Motor Instances ===" -ForegroundColor Cyan
try {
    $instances = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-instances" -Headers $headers
    Write-Host "Motor instances count: $($instances.Count)"
    $instances | ConvertTo-Json -Depth 3
} catch {
    Write-Host "Error: $_"
}

Write-Host ""
Write-Host "=== 4. Get Motor Diagnoses ===" -ForegroundColor Cyan
try {
    $diagnoses = Invoke-RestMethod -Uri "http://localhost:5000/api/motor-diagnoses" -Headers $headers
    Write-Host "Motor diagnoses count: $($diagnoses.Count)"
    $diagnoses | ConvertTo-Json -Depth 5
} catch {
    Write-Host "Error: $_"
}
