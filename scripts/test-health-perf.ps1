$token = $env:ACCESS_TOKEN
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
