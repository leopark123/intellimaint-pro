using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class TagRepository : ITagRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbExecutor _db;
    private readonly ILogger<TagRepository> _logger;

    public TagRepository(IDbExecutor db, ILogger<TagRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagDto>> ListAllAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT tag_id, device_id, name, description, unit, data_type, enabled,
       address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
FROM tag
ORDER BY device_id, COALESCE(tag_group, ''), COALESCE(name, ''), tag_id;";

        var rows = await _db.QueryAsync(sql, MapTag, new { }, ct);
        return rows;
    }

    public async Task<IReadOnlyList<TagDto>> ListByDeviceAsync(string deviceId, CancellationToken ct)
    {
        const string sql = @"
SELECT tag_id, device_id, name, description, unit, data_type, enabled,
       address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
FROM tag
WHERE device_id = @DeviceId
ORDER BY COALESCE(tag_group, ''), COALESCE(name, ''), tag_id;";

        var rows = await _db.QueryAsync(sql, MapTag, new { DeviceId = deviceId }, ct);
        return rows;
    }

    public async Task<IReadOnlyList<TagDto>> ListByGroupAsync(string deviceId, string tagGroup, CancellationToken ct)
    {
        const string sql = @"
SELECT tag_id, device_id, name, description, unit, data_type, enabled,
       address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
FROM tag
WHERE device_id = @DeviceId AND tag_group = @TagGroup
ORDER BY COALESCE(name, ''), tag_id;";

        var rows = await _db.QueryAsync(sql, MapTag, new { DeviceId = deviceId, TagGroup = tagGroup }, ct);
        return rows;
    }

    public async Task<TagDto?> GetAsync(string tagId, CancellationToken ct)
    {
        const string sql = @"
SELECT tag_id, device_id, name, description, unit, data_type, enabled,
       address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
FROM tag
WHERE tag_id = @TagId;";

        return await _db.QuerySingleAsync(sql, MapTag, new { TagId = tagId }, ct);
    }

    public async Task UpsertAsync(TagDto tag, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tag.TagId))
            throw new ArgumentException("TagId is required.", nameof(tag));
        if (string.IsNullOrWhiteSpace(tag.DeviceId))
            throw new ArgumentException("DeviceId is required.", nameof(tag));

        // 保留 created_utc
        const string existsSql = @"SELECT created_utc FROM tag WHERE tag_id = @TagId;";
        var existingCreatedUtc = await _db.ExecuteScalarAsync<long?>(existsSql, new { TagId = tag.TagId }, ct);

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var metadataJson = SerializeMetadata(tag.Metadata);

        if (existingCreatedUtc is null)
        {
            const string insertSql = @"
INSERT INTO tag (
    tag_id, device_id, name, description, unit, data_type, enabled,
    address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
) VALUES (
    @TagId, @DeviceId, @Name, @Description, @Unit, @DataType, @Enabled,
    @Address, @ScanIntervalMs, @TagGroup, @Metadata, @CreatedUtc, @UpdatedUtc
);";

            var createdUtc = tag.CreatedUtc > 0 ? tag.CreatedUtc : nowUtc;

            var affected = await _db.ExecuteNonQueryAsync(
                insertSql,
                new
                {
                    TagId = tag.TagId,
                    DeviceId = tag.DeviceId,
                    tag.Name,
                    tag.Description,
                    tag.Unit,
                    DataType = (int)tag.DataType,
                    Enabled = tag.Enabled ? 1 : 0,
                    tag.Address,
                    ScanIntervalMs = tag.ScanIntervalMs,
                    TagGroup = tag.TagGroup,
                    Metadata = metadataJson,
                    CreatedUtc = createdUtc,
                    UpdatedUtc = nowUtc
                },
                ct);

            _logger.LogInformation("Inserted tag {TagId} for device {DeviceId}, affected={Affected}", tag.TagId, tag.DeviceId, affected);
        }
        else
        {
            const string updateSql = @"
UPDATE tag
SET device_id = @DeviceId,
    name = @Name,
    description = @Description,
    unit = @Unit,
    data_type = @DataType,
    enabled = @Enabled,
    address = @Address,
    scan_interval_ms = @ScanIntervalMs,
    tag_group = @TagGroup,
    metadata = @Metadata,
    updated_utc = @UpdatedUtc
WHERE tag_id = @TagId;";

            var affected = await _db.ExecuteNonQueryAsync(
                updateSql,
                new
                {
                    TagId = tag.TagId,
                    DeviceId = tag.DeviceId,
                    tag.Name,
                    tag.Description,
                    tag.Unit,
                    DataType = (int)tag.DataType,
                    Enabled = tag.Enabled ? 1 : 0,
                    tag.Address,
                    ScanIntervalMs = tag.ScanIntervalMs,
                    TagGroup = tag.TagGroup,
                    Metadata = metadataJson,
                    UpdatedUtc = nowUtc
                },
                ct);

            _logger.LogInformation("Updated tag {TagId}, affected={Affected}", tag.TagId, affected);
        }
    }

    public async Task DeleteAsync(string tagId, CancellationToken ct)
    {
        const string sql = @"DELETE FROM tag WHERE tag_id = @TagId;";
        var affected = await _db.ExecuteNonQueryAsync(sql, new { TagId = tagId }, ct);
        _logger.LogInformation("Deleted tag {TagId}, affected={Affected}", tagId, affected);
    }

    /// <summary>
    /// v56.1: 批量获取多个设备的标签（避免 N+1 查询）
    /// </summary>
    public async Task<Dictionary<string, List<TagDto>>> ListByDevicesAsync(
        IEnumerable<string> deviceIds,
        CancellationToken ct)
    {
        var deviceIdList = deviceIds.ToList();
        if (deviceIdList.Count == 0)
            return new Dictionary<string, List<TagDto>>();

        // 使用 IN 子句批量查询
        var placeholders = string.Join(", ", deviceIdList.Select((_, i) => $"@DeviceId{i}"));
        var sql = $@"
SELECT tag_id, device_id, name, description, unit, data_type, enabled,
       address, scan_interval_ms, tag_group, metadata, created_utc, updated_utc
FROM tag
WHERE device_id IN ({placeholders})
ORDER BY device_id, COALESCE(tag_group, ''), COALESCE(name, ''), tag_id;";

        var parameters = new Dictionary<string, object>();
        for (var i = 0; i < deviceIdList.Count; i++)
        {
            parameters[$"DeviceId{i}"] = deviceIdList[i];
        }

        var rows = await _db.QueryAsync(sql, MapTag, parameters, ct);

        // 按 DeviceId 分组
        return rows.GroupBy(t => t.DeviceId)
                   .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string? SerializeMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static TagDto MapTag(SqliteDataReader reader)
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
                metadata = null;
            }
        }

        var dataTypeInt = reader.GetInt64(reader.GetOrdinal("data_type"));
        var dataType = Enum.IsDefined(typeof(TagValueType), (int)dataTypeInt)
            ? (TagValueType)(int)dataTypeInt
            : TagValueType.Int32; // 读取容错：避免因历史脏数据导致整个读取失败

        return new TagDto
        {
            TagId = reader.GetString(reader.GetOrdinal("tag_id")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            Name = reader.IsDBNull(reader.GetOrdinal("name")) ? null : reader.GetString(reader.GetOrdinal("name")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
            Unit = reader.IsDBNull(reader.GetOrdinal("unit")) ? null : reader.GetString(reader.GetOrdinal("unit")),
            DataType = dataType,
            Enabled = reader.GetInt64(reader.GetOrdinal("enabled")) == 1,
            Address = reader.IsDBNull(reader.GetOrdinal("address")) ? null : reader.GetString(reader.GetOrdinal("address")),
            ScanIntervalMs = reader.IsDBNull(reader.GetOrdinal("scan_interval_ms")) ? null : (int?)reader.GetInt64(reader.GetOrdinal("scan_interval_ms")),
            TagGroup = reader.IsDBNull(reader.GetOrdinal("tag_group")) ? null : reader.GetString(reader.GetOrdinal("tag_group")),
            Metadata = metadata,
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
            UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
        };
    }
}
