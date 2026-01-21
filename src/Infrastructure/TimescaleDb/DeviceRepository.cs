using System.Text.Json;
using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB Device repository implementation
/// </summary>
public sealed class DeviceRepository : IDeviceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<DeviceRepository> _logger;

    public DeviceRepository(INpgsqlConnectionFactory factory, ILogger<DeviceRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, name, location, protocol, host, port, connection_string,
                   enabled, created_utc, updated_utc, status, description, edge_id
            FROM device
            ORDER BY COALESCE(name, ''), device_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<DeviceRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToDto).ToList();
    }

    public async Task<DeviceDto?> GetAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
            SELECT device_id, name, location, protocol, host, port, connection_string,
                   enabled, created_utc, updated_utc, status, description, edge_id
            FROM device
            WHERE device_id = @DeviceId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<DeviceRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));
        return row is null ? null : MapToDto(row);
    }

    public async Task UpsertAsync(DeviceDto device, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceId))
            throw new ArgumentException("DeviceId is required.", nameof(device));

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // PostgreSQL: INSERT ... ON CONFLICT DO UPDATE
        const string sql = @"
            INSERT INTO device (device_id, name, location, protocol, host, port,
                               connection_string, enabled, status, description, edge_id, created_utc, updated_utc)
            VALUES (@DeviceId, @Name, @Location, @Protocol, @Host, @Port,
                    @ConnectionString, @Enabled, @Status, @Description, @EdgeId, @CreatedUtc, @UpdatedUtc)
            ON CONFLICT (device_id) DO UPDATE SET
                name = EXCLUDED.name,
                location = EXCLUDED.location,
                protocol = EXCLUDED.protocol,
                host = EXCLUDED.host,
                port = EXCLUDED.port,
                connection_string = EXCLUDED.connection_string,
                enabled = EXCLUDED.enabled,
                status = EXCLUDED.status,
                description = EXCLUDED.description,
                edge_id = EXCLUDED.edge_id,
                updated_utc = EXCLUDED.updated_utc";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            device.DeviceId,
            device.Name,
            device.Location,
            device.Protocol,
            device.Host,
            device.Port,
            device.ConnectionString,
            device.Enabled,
            Status = "Unknown",
            Description = (string?)null,
            EdgeId = device.EdgeId,
            CreatedUtc = device.CreatedUtc > 0 ? device.CreatedUtc : nowUtc,
            UpdatedUtc = nowUtc
        }, cancellationToken: ct));

        _logger.LogInformation("Upserted device {DeviceId}, affected={Affected}", device.DeviceId, affected);
    }

    public async Task DeleteAsync(string deviceId, CancellationToken ct)
    {
        const string sql = "DELETE FROM device WHERE device_id = @DeviceId";
        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));
        _logger.LogInformation("Deleted device {DeviceId}, affected={Affected}", deviceId, affected);
    }

    private static DeviceDto MapToDto(DeviceRow row) => new()
    {
        DeviceId = row.device_id,
        Name = row.name,
        Location = row.location,
        Protocol = row.protocol,
        Host = row.host,
        Port = row.port,
        ConnectionString = row.connection_string,
        EdgeId = row.edge_id,
        Enabled = row.enabled,
        CreatedUtc = row.created_utc,
        UpdatedUtc = row.updated_utc
    };

    // Dapper mapping class - using class with properties for proper column-name mapping
    private sealed class DeviceRow
    {
        public string device_id { get; set; } = "";
        public string? name { get; set; }
        public string? location { get; set; }
        public string? protocol { get; set; }
        public string? host { get; set; }
        public int? port { get; set; }
        public string? connection_string { get; set; }
        public string? edge_id { get; set; }
        public bool enabled { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
        public string? status { get; set; }
        public string? description { get; set; }
    }
}
