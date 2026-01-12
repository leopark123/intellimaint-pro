$loginResult = Invoke-RestMethod -Uri 'http://localhost:5000/api/auth/login' -Method POST -ContentType 'application/json' -Body '{"username":"admin","password":"admin123"}'
$token = $loginResult.data.token
Write-Host "Token obtained: $($token.Substring(0, 30))..."

$headers = @{Authorization="Bearer $token"}
$result = Invoke-RestMethod -Uri 'http://localhost:5000/api/telemetry/tags' -Headers $headers
Write-Host "Tags API result:"
$result | ConvertTo-Json -Depth 10
