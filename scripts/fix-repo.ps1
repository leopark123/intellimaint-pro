$file = "E:/DAYDAYUP/intellimaint-pro-v56/src/Infrastructure/TimescaleDb/TelemetryRepository.cs"
$content = Get-Content $file -Raw

$oldPattern = @"
CASE t.data_type
                    WHEN 'Float32' THEN 10
                    WHEN 'Float64' THEN 11
                    WHEN 'Int32' THEN 6
                    WHEN 'Int16' THEN 4
                    WHEN 'Bool' THEN 1
                    WHEN 'String' THEN 12
                    ELSE 10
                END as value_type
"@

$newPattern = "t.data_type as value_type"

$content = $content -replace [regex]::Escape($oldPattern), $newPattern
Set-Content $file $content -NoNewline

Write-Host "File updated successfully"
