using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Npgsql;

Console.WriteLine("=== IntelliMaint SQLite to TimescaleDB Migration Tool (Optimized) ===\n");

// Configuration
var sqlitePath = args.Length > 0 ? args[0] : @"E:\DAYDAYUP\intellimaint-pro-v56\src\Host.Edge\data\intellimaint.db";
var pgConnStr = args.Length > 1 ? args[1] : "Host=localhost;Port=5432;Database=intellimaint;Username=intellimaint;Password=IntelliMaint2024!";

if (!File.Exists(sqlitePath))
{
    Console.WriteLine($"SQLite database not found: {sqlitePath}");
    return 1;
}

Console.WriteLine($"SQLite: {sqlitePath}");
Console.WriteLine($"PostgreSQL: {pgConnStr.Split(';').First()}...\n");

using var sqlite = new SqliteConnection($"Data Source={sqlitePath}");
await sqlite.OpenAsync();

using var pg = new NpgsqlConnection(pgConnStr);
await pg.OpenAsync();

// Get SQLite tables
var tables = (await sqlite.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")).ToList();
Console.WriteLine($"Found {tables.Count} tables in SQLite\n");

// Helper functions
static int GetInt(IDictionary<string, object> d, string name, int defaultVal = 0)
{
    if (!d.TryGetValue(name, out var val) || val == null) return defaultVal;
    if (val is long l) return (int)l;
    if (val is int i) return i;
    if (val is string s && int.TryParse(s, out var parsed)) return parsed;
    return defaultVal;
}

static long GetLong(IDictionary<string, object> d, string name, long defaultVal = 0)
{
    if (!d.TryGetValue(name, out var val) || val == null) return defaultVal;
    if (val is long l) return l;
    if (val is int i) return i;
    if (val is string s && long.TryParse(s, out var parsed)) return parsed;
    return defaultVal;
}

static long? GetNullableLong(IDictionary<string, object> d, string name)
{
    if (!d.TryGetValue(name, out var val) || val == null) return null;
    if (val is long l) return l;
    if (val is int i) return i;
    if (val is string s && long.TryParse(s, out var parsed)) return parsed;
    return null;
}

static double GetDouble(IDictionary<string, object> d, string name, double defaultVal = 0)
{
    if (!d.TryGetValue(name, out var val) || val == null) return defaultVal;
    if (val is double dbl) return dbl;
    if (val is float f) return f;
    if (val is long l) return l;
    if (val is int i) return i;
    if (val is string s && double.TryParse(s, out var parsed)) return parsed;
    return defaultVal;
}

static double? GetNullableDouble(IDictionary<string, object> d, string name)
{
    if (!d.TryGetValue(name, out var val) || val == null) return null;
    if (val is double dbl) return dbl;
    if (val is float f) return f;
    if (val is long l) return l;
    if (val is int i) return i;
    if (val is string s && double.TryParse(s, out var parsed)) return parsed;
    return null;
}

static string? GetString(IDictionary<string, object> d, string name)
{
    if (!d.TryGetValue(name, out var val) || val == null) return null;
    return val.ToString();
}

static bool GetBool(IDictionary<string, object> d, string name, bool defaultVal = false)
{
    if (!d.TryGetValue(name, out var val) || val == null) return defaultVal;
    if (val is bool b) return b;
    if (val is long l) return l != 0;
    if (val is int i) return i != 0;
    if (val is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
    return defaultVal;
}

// Simple migrations for small tables
async Task<int> MigrateDevicesAsync()
{
    Console.Write("Migrating devices... ");
    var devices = await sqlite.QueryAsync("SELECT * FROM device");
    var count = 0;
    foreach (IDictionary<string, object> d in devices)
    {
        try
        {
            await pg.ExecuteAsync(@"
                INSERT INTO device (device_id, name, description, type, location, protocol_type, connection_config, status, created_utc, updated_utc)
                VALUES (@device_id, @name, @description, @type, @location, @protocol_type, @connection_config, @status, @created_utc, @updated_utc)
                ON CONFLICT (device_id) DO UPDATE SET name = EXCLUDED.name, updated_utc = EXCLUDED.updated_utc",
                new
                {
                    device_id = GetString(d, "device_id"),
                    name = GetString(d, "name"),
                    description = GetString(d, "description"),
                    type = GetString(d, "type"),
                    location = GetString(d, "location"),
                    protocol_type = GetInt(d, "protocol_type"),
                    connection_config = GetString(d, "connection_config"),
                    status = GetInt(d, "status"),
                    created_utc = GetLong(d, "created_utc"),
                    updated_utc = GetLong(d, "updated_utc")
                });
            count++;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }
    }
    Console.WriteLine($"{count} records");
    return count;
}

async Task<int> MigrateTagsAsync()
{
    Console.Write("Migrating tags... ");
    var tags = await sqlite.QueryAsync("SELECT * FROM tag");
    var count = 0;
    foreach (IDictionary<string, object> t in tags)
    {
        try
        {
            await pg.ExecuteAsync(@"
                INSERT INTO tag (tag_id, device_id, name, description, data_type, address, unit, scale_factor, ""offset"", is_enabled, scan_interval_ms, metadata, created_utc, updated_utc)
                VALUES (@tag_id, @device_id, @name, @description, @data_type, @address, @unit, @scale_factor, @offset, @is_enabled, @scan_interval_ms, @metadata, @created_utc, @updated_utc)
                ON CONFLICT (tag_id) DO UPDATE SET name = EXCLUDED.name, updated_utc = EXCLUDED.updated_utc",
                new
                {
                    tag_id = GetString(t, "tag_id"),
                    device_id = GetString(t, "device_id"),
                    name = GetString(t, "name"),
                    description = GetString(t, "description"),
                    data_type = GetInt(t, "data_type"),
                    address = GetString(t, "address"),
                    unit = GetString(t, "unit"),
                    scale_factor = GetDouble(t, "scale_factor", 1.0),
                    offset = GetDouble(t, "offset", 0.0),
                    is_enabled = GetBool(t, "is_enabled", true),
                    scan_interval_ms = GetInt(t, "scan_interval_ms", 1000),
                    metadata = GetString(t, "metadata"),
                    created_utc = GetLong(t, "created_utc"),
                    updated_utc = GetLong(t, "updated_utc")
                });
            count++;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }
    }
    Console.WriteLine($"{count} records");
    return count;
}

async Task<int> MigrateUsersAsync()
{
    Console.Write("Migrating users... ");
    var users = await sqlite.QueryAsync("SELECT * FROM user");
    var count = 0;
    foreach (IDictionary<string, object> u in users)
    {
        try
        {
            var roleVal = u.TryGetValue("role", out var rv) ? rv : null;
            int role = roleVal switch
            {
                long l => (int)l,
                int i => i,
                string s => s.ToLower() switch { "admin" => 0, "operator" => 1, _ => 2 },
                _ => 2
            };

            await pg.ExecuteAsync(@"
                INSERT INTO ""user"" (user_id, username, password_hash, display_name, role, is_active, created_utc, updated_utc, last_login_utc)
                VALUES (@user_id, @username, @password_hash, @display_name, @role, @is_active, @created_utc, @updated_utc, @last_login_utc)
                ON CONFLICT (user_id) DO UPDATE SET updated_utc = EXCLUDED.updated_utc",
                new
                {
                    user_id = GetString(u, "user_id"),
                    username = GetString(u, "username"),
                    password_hash = GetString(u, "password_hash"),
                    display_name = GetString(u, "display_name"),
                    role,
                    is_active = GetBool(u, "is_active", true),
                    created_utc = GetLong(u, "created_utc"),
                    updated_utc = GetLong(u, "updated_utc"),
                    last_login_utc = GetNullableLong(u, "last_login_utc")
                });
            count++;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }
    }
    Console.WriteLine($"{count} records");
    return count;
}

async Task<int> MigrateAlarmRulesAsync()
{
    Console.Write("Migrating alarm_rules... ");
    if (!tables.Contains("alarm_rule")) { Console.WriteLine("not found"); return 0; }
    var rules = await sqlite.QueryAsync("SELECT * FROM alarm_rule");
    var count = 0;
    foreach (IDictionary<string, object> r in rules)
    {
        try
        {
            var condTypeVal = r.TryGetValue("condition_type", out var ct) ? ct : null;
            int condType = condTypeVal switch
            {
                long l => (int)l,
                int i => i,
                string s => s.ToLower() switch { "threshold" => 0, "rateofchange" or "roc" => 1, "offline" => 2, "deviation" => 3, "volatility" => 4, _ => 0 },
                _ => 0
            };

            var compVal = r.TryGetValue("comparison", out var cv) ? cv : null;
            int comparison = compVal switch
            {
                long l => (int)l,
                int i => i,
                string s => s.ToLower() switch { "greaterthan" or "gt" => 0, "lessthan" or "lt" => 1, _ => 0 },
                _ => 0
            };

            await pg.ExecuteAsync(@"
                INSERT INTO alarm_rule (rule_id, name, description, device_id, tag_id, condition_type, threshold, comparison, severity, is_enabled, cooldown_sec, created_utc, updated_utc)
                VALUES (@rule_id, @name, @description, @device_id, @tag_id, @condition_type, @threshold, @comparison, @severity, @is_enabled, @cooldown_sec, @created_utc, @updated_utc)
                ON CONFLICT (rule_id) DO UPDATE SET updated_utc = EXCLUDED.updated_utc",
                new
                {
                    rule_id = GetString(r, "rule_id"),
                    name = GetString(r, "name"),
                    description = GetString(r, "description"),
                    device_id = GetString(r, "device_id"),
                    tag_id = GetString(r, "tag_id"),
                    condition_type = condType,
                    threshold = GetDouble(r, "threshold"),
                    comparison,
                    severity = GetInt(r, "severity", 1),
                    is_enabled = GetBool(r, "is_enabled", true),
                    cooldown_sec = GetInt(r, "cooldown_sec", 60),
                    created_utc = GetLong(r, "created_utc"),
                    updated_utc = GetLong(r, "updated_utc")
                });
            count++;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }
    }
    Console.WriteLine($"{count} records");
    return count;
}

async Task<int> MigrateAlarmsAsync()
{
    Console.Write("Migrating alarms... ");
    if (!tables.Contains("alarm")) { Console.WriteLine("not found"); return 0; }
    var alarms = await sqlite.QueryAsync("SELECT * FROM alarm");
    var count = 0;
    foreach (IDictionary<string, object> a in alarms)
    {
        try
        {
            await pg.ExecuteAsync(@"
                INSERT INTO alarm (alarm_id, device_id, tag_id, rule_id, severity, code, message, value, status, occurred_utc, created_utc, updated_utc)
                VALUES (@alarm_id, @device_id, @tag_id, @rule_id, @severity, @code, @message, @value, @status, @occurred_utc, @created_utc, @updated_utc)
                ON CONFLICT (alarm_id) DO NOTHING",
                new
                {
                    alarm_id = GetString(a, "alarm_id"),
                    device_id = GetString(a, "device_id"),
                    tag_id = GetString(a, "tag_id"),
                    rule_id = GetString(a, "rule_id"),
                    severity = GetInt(a, "severity", 1),
                    code = GetString(a, "code"),
                    message = GetString(a, "message"),
                    value = GetNullableDouble(a, "value"),
                    status = GetInt(a, "status"),
                    occurred_utc = GetNullableLong(a, "occurred_utc") ?? GetLong(a, "created_utc"),
                    created_utc = GetLong(a, "created_utc"),
                    updated_utc = GetLong(a, "updated_utc")
                });
            count++;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }
    }
    Console.WriteLine($"{count} records");
    return count;
}

async Task<int> MigrateSystemSettingsAsync()
{
    Console.Write("Migrating system_settings... ");
    if (!tables.Contains("system_setting")) { Console.WriteLine("not found"); return 0; }
    var settings = await sqlite.QueryAsync("SELECT * FROM system_setting");
    var count = 0;
    foreach (IDictionary<string, object> s in settings)
    {
        try
        {
            await pg.ExecuteAsync(@"
                INSERT INTO system_setting (key, value, description, updated_utc)
                VALUES (@key, @value, @description, @updated_utc)
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_utc = EXCLUDED.updated_utc",
                new
                {
                    key = GetString(s, "key"),
                    value = GetString(s, "value"),
                    description = GetString(s, "description"),
                    updated_utc = GetLong(s, "updated_utc")
                });
            count++;
        }
        catch (Exception ex) { Console.WriteLine($"\n  Error: {ex.Message}"); }
    }
    Console.WriteLine($"{count} records");
    return count;
}

// Optimized bulk telemetry migration using COPY
async Task<int> MigrateTelemetryBulkAsync()
{
    Console.Write("Migrating telemetry (bulk)... ");
    if (!tables.Contains("telemetry")) { Console.WriteLine("not found"); return 0; }

    var total = await sqlite.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM telemetry");
    Console.WriteLine($"({total:N0} total records)");
    if (total == 0) return 0;

    // Clear existing telemetry data first for clean migration
    await pg.ExecuteAsync("TRUNCATE TABLE telemetry");

    const int batchSize = 50000;
    var count = 0L;
    var offset = 0L;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    while (offset < total)
    {
        var batch = (await sqlite.QueryAsync($"SELECT * FROM telemetry ORDER BY ts LIMIT {batchSize} OFFSET {offset}")).ToList();
        if (batch.Count == 0) break;

        // Use COPY for bulk insert
        using (var writer = await pg.BeginBinaryImportAsync(
            "COPY telemetry (device_id, tag_id, ts, seq, value_type, bool_value, int32_value, int64_value, float32_value, float64_value, string_value, quality) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (IDictionary<string, object> t in batch)
            {
                await writer.StartRowAsync();
                await writer.WriteAsync(GetString(t, "device_id") ?? "", NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(GetString(t, "tag_id") ?? "", NpgsqlTypes.NpgsqlDbType.Text);
                await writer.WriteAsync(GetLong(t, "ts"), NpgsqlTypes.NpgsqlDbType.Bigint);
                await writer.WriteAsync(GetInt(t, "seq"), NpgsqlTypes.NpgsqlDbType.Integer);
                await writer.WriteAsync(GetInt(t, "value_type"), NpgsqlTypes.NpgsqlDbType.Integer);

                // Handle nullable values
                var boolVal = t.TryGetValue("bool_value", out var bv) && bv != null;
                if (boolVal) await writer.WriteAsync(GetBool(t, "bool_value"), NpgsqlTypes.NpgsqlDbType.Boolean);
                else await writer.WriteNullAsync();

                var int32Val = t.TryGetValue("int32_value", out var i32v) && i32v != null;
                if (int32Val) await writer.WriteAsync(GetInt(t, "int32_value"), NpgsqlTypes.NpgsqlDbType.Integer);
                else await writer.WriteNullAsync();

                var int64Val = GetNullableLong(t, "int64_value");
                if (int64Val.HasValue) await writer.WriteAsync(int64Val.Value, NpgsqlTypes.NpgsqlDbType.Bigint);
                else await writer.WriteNullAsync();

                var float32Val = t.TryGetValue("float32_value", out var f32v) && f32v != null;
                if (float32Val) await writer.WriteAsync((float)GetDouble(t, "float32_value"), NpgsqlTypes.NpgsqlDbType.Real);
                else await writer.WriteNullAsync();

                var float64Val = GetNullableDouble(t, "float64_value");
                if (float64Val.HasValue) await writer.WriteAsync(float64Val.Value, NpgsqlTypes.NpgsqlDbType.Double);
                else await writer.WriteNullAsync();

                var strVal = GetString(t, "string_value");
                if (strVal != null) await writer.WriteAsync(strVal, NpgsqlTypes.NpgsqlDbType.Text);
                else await writer.WriteNullAsync();

                await writer.WriteAsync(GetInt(t, "quality", 192), NpgsqlTypes.NpgsqlDbType.Integer);
                count++;
            }
            await writer.CompleteAsync();
        }

        offset += batchSize;
        var elapsed = sw.Elapsed;
        var rate = count / elapsed.TotalSeconds;
        var eta = TimeSpan.FromSeconds((total - count) / rate);
        Console.Write($"\r  Migrating telemetry... {count:N0}/{total:N0} ({count * 100 / total}%) - {rate:N0}/s - ETA: {eta:mm\\:ss}    ");
    }

    sw.Stop();
    Console.WriteLine($"\r  Migrating telemetry... {count:N0} records in {sw.Elapsed:mm\\:ss} ({count / sw.Elapsed.TotalSeconds:N0}/s)                    ");
    return (int)Math.Min(count, int.MaxValue);
}

// Run migrations
Console.WriteLine("Starting migration...\n");

var totalMigrated = 0;
totalMigrated += await MigrateDevicesAsync();
totalMigrated += await MigrateTagsAsync();
totalMigrated += await MigrateUsersAsync();
totalMigrated += await MigrateAlarmRulesAsync();
totalMigrated += await MigrateAlarmsAsync();
totalMigrated += await MigrateSystemSettingsAsync();
totalMigrated += await MigrateTelemetryBulkAsync();

Console.WriteLine($"\n=== Migration Complete ===");
Console.WriteLine($"Total records migrated: {totalMigrated:N0}");

// Verify
Console.WriteLine("\nVerification (PostgreSQL):");
var pgDevices = await pg.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM device");
var pgTags = await pg.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM tag");
var pgUsers = await pg.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM \"user\"");
var pgTelemetry = await pg.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM telemetry");
var pgAlarms = await pg.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM alarm");

Console.WriteLine($"  Devices: {pgDevices}");
Console.WriteLine($"  Tags: {pgTags}");
Console.WriteLine($"  Users: {pgUsers}");
Console.WriteLine($"  Telemetry: {pgTelemetry:N0}");
Console.WriteLine($"  Alarms: {pgAlarms}");

return 0;
