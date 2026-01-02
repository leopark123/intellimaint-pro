using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// Device repository implementation using SQLite
/// </summary>
public sealed class DeviceRepository : IDeviceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbExecutor _db;
    private readonly ILogger<DeviceRepository> _logger;

    public DeviceRepository(IDbExecutor db, ILogger<DeviceRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT device_id, name, location, model, protocol, host, port, connection_string,
       enabled, metadata, created_utc, updated_utc
FROM device
ORDER BY COALESCE(name, ''), device_id;";

        var rows = await _db.QueryAsync(sql, MapDevice, parameters: null, ct);
        return rows;
    }

    public async Task<DeviceDto?> GetAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
SELECT device_id, name, location, model, protocol, host, port, connection_string,
       enabled, metadata, created_utc, updated_utc
FROM device
WHERE device_id = @DeviceId;";

        return await _db.QuerySingleAsync(sql, MapDevice, new { DeviceId = deviceId }, ct);
    }

    public async Task UpsertAsync(DeviceDto device, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceId))
            throw new ArgumentException("DeviceId is required.", nameof(device));

        // Check if device exists (get created_utc to preserve it)
        const string existsSql = @"SELECT created_utc FROM device WHERE device_id = @DeviceId;";
        var existingCreatedUtc = await _db.ExecuteScalarAsync<long?>(existsSql, new { DeviceId = device.DeviceId }, ct);

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var metadataJson = SerializeMetadata(device.Metadata);

        if (existingCreatedUtc is null)
        {
            // INSERT
            const string insertSql = @"
INSERT INTO device (device_id, name, location, model, protocol, host, port, connection_string, enabled, metadata, created_utc, updated_utc)
VALUES (@DeviceId, @Name, @Location, @Model, @Protocol, @Host, @Port, @ConnectionString, @Enabled, @Metadata, @CreatedUtc, @UpdatedUtc);";

            var createdUtc = device.CreatedUtc > 0 ? device.CreatedUtc : nowUtc;

            var affected = await _db.ExecuteNonQueryAsync(
                insertSql,
                new
                {
                    DeviceId = device.DeviceId,
                    device.Name,
                    device.Location,
                    device.Model,
                    device.Protocol,
                    device.Host,
                    device.Port,
                    device.ConnectionString,
                    Enabled = device.Enabled ? 1 : 0,
                    Metadata = metadataJson,
                    CreatedUtc = createdUtc,
                    UpdatedUtc = nowUtc
                },
                ct);

            _logger.LogInformation("Inserted device {DeviceId}, affected={Affected}", device.DeviceId, affected);
        }
        else
        {
            // UPDATE (preserve created_utc)
            const string updateSql = @"
UPDATE device
SET name = @Name,
    location = @Location,
    model = @Model,
    protocol = @Protocol,
    host = @Host,
    port = @Port,
    connection_string = @ConnectionString,
    enabled = @Enabled,
    metadata = @Metadata,
    updated_utc = @UpdatedUtc
WHERE device_id = @DeviceId;";

            var affected = await _db.ExecuteNonQueryAsync(
                updateSql,
                new
                {
                    DeviceId = device.DeviceId,
                    device.Name,
                    device.Location,
                    device.Model,
                    device.Protocol,
                    device.Host,
                    device.Port,
                    device.ConnectionString,
                    Enabled = device.Enabled ? 1 : 0,
                    Metadata = metadataJson,
                    UpdatedUtc = nowUtc
                },
                ct);

            _logger.LogInformation("Updated device {DeviceId}, affected={Affected}", device.DeviceId, affected);
        }
    }

    public async Task DeleteAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"DELETE FROM device WHERE device_id = @DeviceId;";
        var affected = await _db.ExecuteNonQueryAsync(sql, new { DeviceId = deviceId }, ct);
        _logger.LogInformation("Deleted device {DeviceId}, affected={Affected}", deviceId, affected);
    }

    private static string? SerializeMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static DeviceDto MapDevice(SqliteDataReader reader)
    {
        var metadataJson = reader.IsDBNull(reader.GetOrdinal("metadata"))
            ? null
            : reader.GetString(reader.GetOrdinal("metadata"));

        Dictionary<string, string>? metadata = null;
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson, JsonOptions);
            }
            catch
            {
                // Fault tolerance: if metadata parsing fails, set to null
                metadata = null;
            }
        }

        return new DeviceDto
        {
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            Name = reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString(reader.GetOrdinal("name")),
            Location = reader.IsDBNull(reader.GetOrdinal("location")) ? null : reader.GetString(reader.GetOrdinal("location")),
            Model = reader.IsDBNull(reader.GetOrdinal("model")) ? null : reader.GetString(reader.GetOrdinal("model")),
            Protocol = reader.IsDBNull(reader.GetOrdinal("protocol")) ? null : reader.GetString(reader.GetOrdinal("protocol")),
            Host = reader.IsDBNull(reader.GetOrdinal("host")) ? null : reader.GetString(reader.GetOrdinal("host")),
            Port = reader.IsDBNull(reader.GetOrdinal("port")) ? null : reader.GetInt32(reader.GetOrdinal("port")),
            ConnectionString = reader.IsDBNull(reader.GetOrdinal("connection_string")) ? null : reader.GetString(reader.GetOrdinal("connection_string")),
            Enabled = reader.GetInt64(reader.GetOrdinal("enabled")) == 1,
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc")),
            Metadata = metadata
        };
    }
}
