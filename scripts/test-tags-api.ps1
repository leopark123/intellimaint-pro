$loginResult = Invoke-RestMethod -Uri 'http://localhost:5000/api/auth/login' -Method POST -ContentType 'application/json' -Body (@{ username = $env:ADMIN_USERNAME; password = $env:ADMIN_PASSWORD } | ConvertTo-Json -Compress)
$token = $loginResult.data.token
if ([string]::IsNullOrWhiteSpace($token)) { throw "Authentication failed: no access credential returned" }
Write-Host "Authentication succeeded"

$headers = @{Authorization="Bearer $token"}
$result = Invoke-RestMethod -Uri 'http://localhost:5000/api/telemetry/tags' -Headers $headers
Write-Host "Tags API result:"
$result | ConvertTo-Json -Depth 10
