using System.Text.Json;
using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB Tag repository implementation
/// </summary>
public sealed class TagRepository : ITagRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<TagRepository> _logger;

    public TagRepository(INpgsqlConnectionFactory factory, ILogger<TagRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagDto>> ListAllAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT tag_id, device_id, name, description, unit, data_type, enabled,
                   address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
            FROM tag
            ORDER BY device_id, COALESCE(tag_group, ''), COALESCE(name, ''), tag_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TagRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(MapToTag).ToList();
    }

    public async Task<IReadOnlyList<TagDto>> ListByDeviceAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
            SELECT tag_id, device_id, name, description, unit, data_type, enabled,
                   address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
            FROM tag
            WHERE device_id = @DeviceId
            ORDER BY COALESCE(tag_group, ''), COALESCE(name, ''), tag_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TagRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: ct));
        return rows.Select(MapToTag).ToList();
    }

    public async Task<IReadOnlyList<TagDto>> ListByGroupAsync(string deviceId, string tagGroup, CancellationToken ct)
    {
        const string sql = @"
            SELECT tag_id, device_id, name, description, unit, data_type, enabled,
                   address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
            FROM tag
            WHERE device_id = @DeviceId AND tag_group = @TagGroup
            ORDER BY COALESCE(name, ''), tag_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TagRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId, TagGroup = tagGroup }, cancellationToken: ct));
        return rows.Select(MapToTag).ToList();
    }

    public async Task<TagDto?> GetAsync(string tagId, CancellationToken ct)
    {
        const string sql = @"
            SELECT tag_id, device_id, name, description, unit, data_type, enabled,
                   address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
            FROM tag
            WHERE tag_id = @TagId";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<TagRow>(
            new CommandDefinition(sql, new { TagId = tagId }, cancellationToken: ct));
        return row is null ? null : MapToTag(row);
    }

    public async Task UpsertAsync(TagDto tag, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tag.TagId))
            throw new ArgumentException("TagId is required.", nameof(tag));
        if (string.IsNullOrWhiteSpace(tag.DeviceId))
            throw new ArgumentException("DeviceId is required.", nameof(tag));

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var metadataJson = SerializeMetadata(tag.Metadata);

        // Check if exists to preserve created_utc
        const string existsSql = "SELECT created_utc FROM tag WHERE tag_id = @TagId";
        using var conn = _factory.CreateConnection();
        var existingCreatedUtc = await conn.ExecuteScalarAsync<long?>(
            new CommandDefinition(existsSql, new { tag.TagId }, cancellationToken: ct));

        const string sql = @"
            INSERT INTO tag (
                tag_id, device_id, name, description, unit, data_type, enabled,
                address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
            ) VALUES (
                @TagId, @DeviceId, @Name, @Description, @Unit, @DataType, @Enabled,
                @Address, @ScanIntervalMs, @TagGroup, @Metadata, @CreatedUtc, @UpdatedUtc
            )
            ON CONFLICT (tag_id) DO UPDATE SET
                device_id = EXCLUDED.device_id,
                name = EXCLUDED.name,
                description = EXCLUDED.description,
                unit = EXCLUDED.unit,
                data_type = EXCLUDED.data_type,
                enabled = EXCLUDED.enabled,
                address = EXCLUDED.address,
                scan_interval_ms = EXCLUDED.scan_interval_ms,
                tag_group = EXCLUDED.tag_group,
                metadata = EXCLUDED.metadata,
                updated_utc = EXCLUDED.updated_utc";

        var createdUtc = existingCreatedUtc ?? (tag.CreatedUtc > 0 ? tag.CreatedUtc : nowUtc);

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            tag.TagId,
            tag.DeviceId,
            tag.Name,
            tag.Description,
            tag.Unit,
            DataType = (int)tag.DataType,
            tag.Enabled,
            tag.Address,
            tag.ScanIntervalMs,
            tag.TagGroup,
            Metadata = metadataJson,
            CreatedUtc = createdUtc,
            UpdatedUtc = nowUtc
        }, cancellationToken: ct));

        _logger.LogDebug("Upserted tag {TagId} for device {DeviceId}", tag.TagId, tag.DeviceId);
    }

    public async Task DeleteAsync(string tagId, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag WHERE tag_id = @TagId";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { TagId = tagId }, cancellationToken: ct));

        _logger.LogInformation("Deleted tag {TagId}, affected={Affected}", tagId, affected);
    }

    public async Task<Dictionary<string, List<TagDto>>> ListByDevicesAsync(
        IEnumerable<string> deviceIds,
        CancellationToken ct)
    {
        var deviceIdList = deviceIds.ToList();
        if (deviceIdList.Count == 0)
            return new Dictionary<string, List<TagDto>>();

        // Use ANY instead of IN for PostgreSQL with arrays
        const string sql = @"
            SELECT tag_id, device_id, name, description, unit, data_type, enabled,
                   address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
            FROM tag
            WHERE device_id = ANY(@DeviceIds)
            ORDER BY device_id, COALESCE(tag_group, ''), COALESCE(name, ''), tag_id";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<TagRow>(
            new CommandDefinition(sql, new { DeviceIds = deviceIdList.ToArray() }, cancellationToken: ct));

        return rows
            .Select(MapToTag)
            .GroupBy(t => t.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string? SerializeMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static TagDto MapToTag(TagRow row)
    {
        Dictionary<string, string>? metadata = null;
        if (!string.IsNullOrWhiteSpace(row.metadata))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(row.metadata, JsonOptions);
            }
            catch { /* ignore parse errors */ }
        }

        var dataTypeInt = row.data_type;
        var dataType = Enum.IsDefined(typeof(TagValueType), dataTypeInt)
            ? (TagValueType)dataTypeInt
            : TagValueType.Int32;

        return new TagDto
        {
            TagId = row.tag_id,
            DeviceId = row.device_id,
            Name = row.name,
            Description = row.description,
            Unit = row.unit,
            DataType = dataType,
            Enabled = row.enabled,
            Address = row.address,
            ScanIntervalMs = row.scan_interval_ms,
            TagGroup = row.tag_group,
            Metadata = metadata,
            CreatedUtc = row.created_utc,
            UpdatedUtc = row.updated_utc
        };
    }

    // Dapper mapping class - using class with properties for proper column-name mapping
    private sealed class TagRow
    {
        public string tag_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string? name { get; set; }
        public string? description { get; set; }
        public string? unit { get; set; }
        public int data_type { get; set; }
        public bool enabled { get; set; }
        public string? address { get; set; }
        public int? scan_interval_ms { get; set; }
        public string? tag_group { get; set; }
        public string? metadata { get; set; }
        public long created_utc { get; set; }
        public long updated_utc { get; set; }
    }
}
