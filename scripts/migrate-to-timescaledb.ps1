# ========================================================================
# SQLite to TimescaleDB Migration Script
# IntelliMaint Pro v56
# ========================================================================
# Usage:
#   .\migrate-to-timescaledb.ps1 [-SqlitePath <path>] [-PgHost <host>]
#                                [-PgPort <port>] [-PgDb <db>]
#                                [-PgUser <user>] [-PgPassword <pwd>]
#                                [-BatchSize <size>] [-DryRun]
# ========================================================================

param(
    [string]$SqlitePath = ".\src\Host.Edge\data\intellimaint.db",
    [string]$PgHost = "localhost",
    [string]$PgPort = "5432",
    [string]$PgDb = "intellimaint",
    [string]$PgUser = "intellimaint",
    [string]$PgPassword = "IntelliMaint2024!",
    [int]$BatchSize = 10000,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# Color output helpers
function Write-Info { Write-Host $args[0] -ForegroundColor Cyan }
function Write-Success { Write-Host $args[0] -ForegroundColor Green }
function Write-Warn { Write-Host $args[0] -ForegroundColor Yellow }
function Write-Err { Write-Host $args[0] -ForegroundColor Red }

Write-Host ""
Write-Host "=============================================" -ForegroundColor Magenta
Write-Host "  SQLite -> TimescaleDB Migration Script" -ForegroundColor Magenta
Write-Host "  IntelliMaint Pro v56" -ForegroundColor Magenta
Write-Host "=============================================" -ForegroundColor Magenta
Write-Host ""

# Validate SQLite database exists
if (-not (Test-Path $SqlitePath)) {
    Write-Err "SQLite database not found: $SqlitePath"
    exit 1
}

Write-Info "SQLite Path: $SqlitePath"
Write-Info "PostgreSQL: $PgUser@$PgHost`:$PgPort/$PgDb"
Write-Info "Batch Size: $BatchSize"
if ($DryRun) {
    Write-Warn "DRY RUN MODE - No data will be written"
}
Write-Host ""

# Check for required tools
Write-Info "Checking required tools..."

# Check sqlite3
$sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
if (-not $sqlite3) {
    Write-Err "sqlite3 not found. Please install SQLite CLI tools."
    Write-Host "  Windows: winget install SQLite.SQLite"
    Write-Host "  Or download from: https://sqlite.org/download.html"
    exit 1
}
Write-Success "  sqlite3: OK"

# Check psql
$psql = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psql) {
    Write-Err "psql not found. Please install PostgreSQL CLI tools."
    Write-Host "  Windows: winget install PostgreSQL.PostgreSQL"
    exit 1
}
Write-Success "  psql: OK"

Write-Host ""

# Set PGPASSWORD for psql
$env:PGPASSWORD = $PgPassword

# Function to run psql command
function Invoke-Psql {
    param([string]$Query, [switch]$Quiet)
    $args = @("-h", $PgHost, "-p", $PgPort, "-U", $PgUser, "-d", $PgDb, "-t", "-c", $Query)
    if ($Quiet) {
        psql @args 2>&1 | Out-Null
    } else {
        psql @args 2>&1
    }
}

# Function to get table count from SQLite
function Get-SqliteCount {
    param([string]$TableName)
    $result = sqlite3 $SqlitePath "SELECT COUNT(*) FROM $TableName;" 2>&1
    return [int]$result
}

# Function to get table count from PostgreSQL
function Get-PgCount {
    param([string]$TableName)
    $result = Invoke-Psql "SELECT COUNT(*) FROM `"$TableName`";"
    return [int]$result.Trim()
}

# Test PostgreSQL connection
Write-Info "Testing PostgreSQL connection..."
try {
    $pgVersion = Invoke-Psql "SELECT version();"
    Write-Success "  Connected to PostgreSQL"

    $tsVersion = Invoke-Psql "SELECT extversion FROM pg_extension WHERE extname = 'timescaledb';"
    if ($tsVersion) {
        Write-Success "  TimescaleDB version: $($tsVersion.Trim())"
    } else {
        Write-Warn "  TimescaleDB extension not installed"
        Write-Info "  Run: docker exec -it intellimaint-timescaledb psql -U intellimaint -c 'CREATE EXTENSION IF NOT EXISTS timescaledb;'"
    }
} catch {
    Write-Err "Failed to connect to PostgreSQL: $_"
    exit 1
}

Write-Host ""

# Define tables in migration order (respecting foreign keys)
$tables = @(
    @{ Name = "device"; PKey = "device_id"; BatchMode = $false },
    @{ Name = "tag"; PKey = "tag_id"; BatchMode = $false },
    @{ Name = "alarm_rule"; PKey = "rule_id"; BatchMode = $false },
    @{ Name = "user"; PKey = "user_id"; BatchMode = $false; PgName = '"user"' },
    @{ Name = "system_setting"; PKey = "key"; BatchMode = $false },
    @{ Name = "health_baseline"; PKey = "device_id"; BatchMode = $false },
    @{ Name = "schema_version"; PKey = "version"; BatchMode = $false },
    @{ Name = "telemetry"; OrderBy = "ts"; BatchMode = $true },
    @{ Name = "telemetry_1m"; OrderBy = "ts_bucket"; BatchMode = $true },
    @{ Name = "telemetry_1h"; OrderBy = "ts_bucket"; BatchMode = $true },
    @{ Name = "alarm"; PKey = "alarm_id"; OrderBy = "ts"; BatchMode = $true },
    @{ Name = "alarm_ack"; PKey = "alarm_id"; BatchMode = $false },
    @{ Name = "alarm_group"; PKey = "group_id"; BatchMode = $false },
    @{ Name = "audit_log"; OrderBy = "id"; BatchMode = $true },
    @{ Name = "device_health_snapshot"; OrderBy = "ts"; BatchMode = $true }
)

# Get source data statistics
Write-Info "Analyzing source database..."
$totalRows = 0
$tableStats = @{}

foreach ($table in $tables) {
    try {
        $count = Get-SqliteCount $table.Name
        $tableStats[$table.Name] = $count
        $totalRows += $count
        Write-Host "  $($table.Name): $count rows"
    } catch {
        Write-Warn "  $($table.Name): Table not found or error"
        $tableStats[$table.Name] = 0
    }
}

Write-Host ""
Write-Info "Total rows to migrate: $totalRows"
Write-Host ""

if ($DryRun) {
    Write-Warn "DRY RUN - Exiting without migration"
    exit 0
}

# Confirm migration
$confirm = Read-Host "Proceed with migration? (yes/no)"
if ($confirm -ne "yes") {
    Write-Warn "Migration cancelled"
    exit 0
}

Write-Host ""
Write-Info "Starting migration..."
$startTime = Get-Date
$migratedRows = 0
$errors = @()

foreach ($table in $tables) {
    $tableName = $table.Name
    $pgTableName = if ($table.PgName) { $table.PgName } else { $tableName }
    $sourceCount = $tableStats[$tableName]

    if ($sourceCount -eq 0) {
        Write-Warn "Skipping empty table: $tableName"
        continue
    }

    Write-Info "Migrating: $tableName ($sourceCount rows)..."

    try {
        if ($table.BatchMode) {
            # Batch migration for large tables
            $offset = 0
            $orderBy = if ($table.OrderBy) { $table.OrderBy } else { $table.PKey }

            while ($offset -lt $sourceCount) {
                $remaining = $sourceCount - $offset
                $currentBatch = [Math]::Min($BatchSize, $remaining)

                Write-Host "  Processing batch: $offset - $($offset + $currentBatch) of $sourceCount" -NoNewline

                # Export batch to CSV
                $csvPath = [System.IO.Path]::GetTempFileName()
                $query = "SELECT * FROM $tableName ORDER BY $orderBy LIMIT $BatchSize OFFSET $offset"
                sqlite3 -header -csv $SqlitePath $query > $csvPath

                # Import CSV to PostgreSQL
                $copyCmd = "\COPY $pgTableName FROM '$csvPath' WITH (FORMAT csv, HEADER true)"
                psql -h $PgHost -p $PgPort -U $PgUser -d $PgDb -c $copyCmd 2>&1 | Out-Null

                # Cleanup
                Remove-Item $csvPath -Force

                $offset += $BatchSize
                $migratedRows += $currentBatch
                Write-Host " [OK]" -ForegroundColor Green
            }
        } else {
            # Single batch migration for small tables
            $csvPath = [System.IO.Path]::GetTempFileName()
            $query = "SELECT * FROM $tableName"
            sqlite3 -header -csv $SqlitePath $query > $csvPath

            # Check if file has content
            $csvContent = Get-Content $csvPath
            if ($csvContent.Count -gt 1) {
                $copyCmd = "\COPY $pgTableName FROM '$csvPath' WITH (FORMAT csv, HEADER true)"
                psql -h $PgHost -p $PgPort -U $PgUser -d $PgDb -c $copyCmd 2>&1 | Out-Null
            }

            Remove-Item $csvPath -Force
            $migratedRows += $sourceCount
            Write-Success "  Completed: $sourceCount rows"
        }
    } catch {
        Write-Err "  Error migrating $tableName`: $_"
        $errors += @{ Table = $tableName; Error = $_.ToString() }
    }
}

$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host "=============================================" -ForegroundColor Magenta
Write-Host "  Migration Complete" -ForegroundColor Magenta
Write-Host "=============================================" -ForegroundColor Magenta
Write-Host ""
Write-Info "Duration: $($duration.TotalMinutes.ToString('F2')) minutes"
Write-Info "Rows migrated: $migratedRows / $totalRows"

if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Err "Errors encountered:"
    foreach ($err in $errors) {
        Write-Err "  $($err.Table): $($err.Error)"
    }
}

# Verify migration
Write-Host ""
Write-Info "Verifying migration..."
$verifyErrors = @()

foreach ($table in $tables) {
    $tableName = $table.Name
    $pgTableName = if ($table.PgName) { $table.PgName } else { $tableName }
    $sourceCount = $tableStats[$tableName]

    if ($sourceCount -eq 0) { continue }

    try {
        $targetCount = Get-PgCount $pgTableName
        if ($targetCount -eq $sourceCount) {
            Write-Success "  $tableName`: $sourceCount = $targetCount [OK]"
        } else {
            Write-Warn "  $tableName`: Source=$sourceCount, Target=$targetCount [MISMATCH]"
            $verifyErrors += $tableName
        }
    } catch {
        Write-Err "  $tableName`: Verification failed - $_"
        $verifyErrors += $tableName
    }
}

if ($verifyErrors.Count -eq 0) {
    Write-Host ""
    Write-Success "Migration verified successfully!"
    Write-Host ""
    Write-Info "Next steps:"
    Write-Host "  1. Update appsettings.json: DatabaseProvider = 'TimescaleDb'"
    Write-Host "  2. Restart the application"
    Write-Host "  3. Verify API functionality"
} else {
    Write-Host ""
    Write-Warn "Some tables have mismatched row counts. Please investigate."
}

# Cleanup
$env:PGPASSWORD = $null
