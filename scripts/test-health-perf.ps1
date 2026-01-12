$token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6ImFkbWluMDAwMDAwMDAwMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJhZG1pbiIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IkFkbWluIiwiZGlzcGxheV9uYW1lIjoiQWRtaW5pc3RyYXRvciIsImV4cCI6MTc2NzU3NDQ4NSwiaXNzIjoiSW50ZWxsaU1haW50IiwiYXVkIjoiSW50ZWxsaU1haW50In0.zwLs7k6DdaggxQZNAfyl5RVOSfmTz8F5onEt0M1WIRo"
$baseUrl = "http://localhost:5000/api/health-assessment"
$headers = @{ "Authorization" = "Bearer $token" }

Write-Host "=== Health Assessment API Performance Test (10 requests each) ==="
Write-Host ""

function Test-Api {
    param (
        [string]$Name,
        [string]$Url
    )

    $times = @()
    for ($i = 1; $i -le 10; $i++) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $null = Invoke-RestMethod -Uri $Url -Headers $headers -Method Get -TimeoutSec 30
        } catch {
            Write-Host "Error: $_"
        }
        $stopwatch.Stop()
        $times += $stopwatch.ElapsedMilliseconds
    }

    $avg = ($times | Measure-Object -Average).Average
    $min = ($times | Measure-Object -Minimum).Minimum
    $max = ($times | Measure-Object -Maximum).Maximum

    Write-Host ("{0,-30} Avg: {1:F0}ms, Min: {2}ms, Max: {3}ms" -f $Name, $avg, $min, $max)
}

Test-Api -Name "All Devices Health" -Url "$baseUrl/devices"
Test-Api -Name "Single Device Health" -Url "$baseUrl/devices/SIM-PLC-001"
Test-Api -Name "Health Summary" -Url "$baseUrl/summary"
Test-Api -Name "Device Health History" -Url "$baseUrl/devices/SIM-PLC-001/history"

Write-Host ""
Write-Host "=== Performance Test Complete ==="
