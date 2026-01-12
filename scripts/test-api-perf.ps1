# API Performance Test Script
$token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1laWRlbnRpZmllciI6ImFkbWluMDAwMDAwMDAwMSIsImh0dHA6Ly9zY2hlbWFzLnhtbHNvYXAub3JnL3dzLzIwMDUvMDUvaWRlbnRpdHkvY2xhaW1zL25hbWUiOiJhZG1pbiIsImh0dHA6Ly9zY2hlbWFzLm1pY3Jvc29mdC5jb20vd3MvMjAwOC8wNi9pZGVudGl0eS9jbGFpbXMvcm9sZSI6IkFkbWluIiwiZGlzcGxheV9uYW1lIjoiQWRtaW5pc3RyYXRvciIsImV4cCI6MTc2NzYxOTQzNCwiaXNzIjoiSW50ZWxsaU1haW50IiwiYXVkIjoiSW50ZWxsaU1haW50In0.F62ONSVykLUkVp5cMW9UEgkz6nsC6ARBq4zTpvjUfqY"
$baseUrl = "http://localhost:5000/api"
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

Write-Host "=== API Performance Test ===" -ForegroundColor Cyan
Write-Host ""

$endpoints = @(
    @{ Name = "Devices List"; Url = "/devices" },
    @{ Name = "Tags List"; Url = "/tags" },
    @{ Name = "Alarms (Open)"; Url = "/alarms?status=0&limit=20" },
    @{ Name = "Alarm Stats"; Url = "/alarms/stats" },
    @{ Name = "Health Stats"; Url = "/health/stats" },
    @{ Name = "Health Assessment"; Url = "/health-assessment/devices" },
    @{ Name = "Alarm Rules"; Url = "/alarm-rules" },
    @{ Name = "Users List"; Url = "/users" },
    @{ Name = "Audit Logs"; Url = "/audit-logs?limit=20" }
)

foreach ($ep in $endpoints) {
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-RestMethod -Uri "$baseUrl$($ep.Url)" -Headers $headers -Method Get -TimeoutSec 30
        $sw.Stop()
        $ms = $sw.ElapsedMilliseconds

        $color = if ($ms -lt 100) { "Green" } elseif ($ms -lt 500) { "Yellow" } else { "Red" }
        Write-Host "$($ep.Name.PadRight(25)) : $($ms.ToString().PadLeft(6)) ms" -ForegroundColor $color
    }
    catch {
        Write-Host "$($ep.Name.PadRight(25)) : ERROR - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Database Statistics ===" -ForegroundColor Cyan

try {
    $dbStats = Invoke-RestMethod -Uri "$baseUrl/health/database/statistics" -Headers $headers -Method Get -TimeoutSec 30
    if ($dbStats.success -and $dbStats.data) {
        $stats = $dbStats.data
        Write-Host "Database Size: $([math]::Round($stats.databaseSizeBytes / 1024 / 1024, 2)) MB"
        Write-Host "Total Telemetry Rows: $($stats.totalTelemetryRows)"
        Write-Host "Index Count: $($stats.totalIndexCount)"
        Write-Host ""
        Write-Host "Table Statistics:" -ForegroundColor Yellow
        foreach ($table in $stats.tables) {
            Write-Host "  $($table.tableName.PadRight(20)) : $($table.rowCount.ToString().PadLeft(10)) rows"
        }
    }
}
catch {
    Write-Host "Failed to get database statistics: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Database Health ===" -ForegroundColor Cyan
try {
    $dbHealth = Invoke-RestMethod -Uri "$baseUrl/health/database" -Headers $headers -Method Get -TimeoutSec 30
    if ($dbHealth.success -and $dbHealth.data) {
        $h = $dbHealth.data
        Write-Host "Status: $($h.status)"
        Write-Host "Latency: $($h.latencyMs) ms"
        Write-Host "Writable: $($h.isWritable)"
        if ($h.diagnostics) {
            Write-Host "Journal Mode: $($h.diagnostics.journal_mode)"
            Write-Host "DB Size: $($h.diagnostics.database_size_mb) MB"
            Write-Host "Free Space: $($h.diagnostics.free_space_mb) MB"
        }
    }
}
catch {
    Write-Host "Failed to get database health: $($_.Exception.Message)" -ForegroundColor Red
}
