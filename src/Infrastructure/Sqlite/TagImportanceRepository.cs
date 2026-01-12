using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v61: 标签重要性配置仓储 - SQLite 实现
/// </summary>
public sealed class TagImportanceRepository : ITagImportanceRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<TagImportanceRepository> _logger;

    public TagImportanceRepository(IDbExecutor db, ILogger<TagImportanceRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagImportanceConfig>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, pattern, importance, description, priority, enabled, created_utc, updated_utc
            FROM tag_importance_config
            ORDER BY priority DESC, id ASC";

        return await _db.QueryAsync(sql, MapConfig, new { }, ct);
    }

    public async Task<IReadOnlyList<TagImportanceConfig>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, pattern, importance, description, priority, enabled, created_utc, updated_utc
            FROM tag_importance_config
            WHERE enabled = 1
            ORDER BY priority DESC, id ASC";

        return await _db.QueryAsync(sql, MapConfig, new { }, ct);
    }

    public async Task<TagImportanceConfig?> GetAsync(int id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, pattern, importance, description, priority, enabled, created_utc, updated_utc
            FROM tag_importance_config
            WHERE id = @Id";

        var results = await _db.QueryAsync(sql, MapConfig, new { Id = id }, ct);
        return results.FirstOrDefault();
    }

    public async Task<int> CreateAsync(TagImportanceConfig config, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO tag_importance_config (pattern, importance, description, priority, enabled, created_utc, updated_utc)
            VALUES (@Pattern, @Importance, @Description, @Priority, @Enabled, @CreatedUtc, @UpdatedUtc);
            SELECT last_insert_rowid();";

        var result = await _db.ExecuteScalarAsync<long>(sql, new
        {
            config.Pattern,
            Importance = (int)config.Importance,
            config.Description,
            config.Priority,
            Enabled = config.Enabled ? 1 : 0,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        }, ct);

        var id = (int)result;
        _logger.LogInformation("Created tag importance config {Id}: pattern={Pattern}, importance={Importance}",
            id, config.Pattern, config.Importance);

        return id;
    }

    public async Task UpdateAsync(TagImportanceConfig config, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            UPDATE tag_importance_config
            SET pattern = @Pattern,
                importance = @Importance,
                description = @Description,
                priority = @Priority,
                enabled = @Enabled,
                updated_utc = @UpdatedUtc
            WHERE id = @Id";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            config.Id,
            config.Pattern,
            Importance = (int)config.Importance,
            config.Description,
            config.Priority,
            Enabled = config.Enabled ? 1 : 0,
            UpdatedUtc = nowUtc
        }, ct);

        _logger.LogInformation("Updated tag importance config {Id}", config.Id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag_importance_config WHERE id = @Id";
        await _db.ExecuteNonQueryAsync(sql, new { Id = id }, ct);
        _logger.LogInformation("Deleted tag importance config {Id}", id);
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"
            UPDATE tag_importance_config
            SET enabled = @Enabled, updated_utc = @UpdatedUtc
            WHERE id = @Id";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            Id = id,
            Enabled = enabled ? 1 : 0,
            UpdatedUtc = nowUtc
        }, ct);

        _logger.LogInformation("Set tag importance config {Id} enabled={Enabled}", id, enabled);
    }

    private static TagImportanceConfig MapConfig(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Pattern = reader.GetString(reader.GetOrdinal("pattern")),
        Importance = (TagImportance)reader.GetInt32(reader.GetOrdinal("importance")),
        Description = reader.IsDBNull(reader.GetOrdinal("description"))
            ? null
            : reader.GetString(reader.GetOrdinal("description")),
        Priority = reader.GetInt32(reader.GetOrdinal("priority")),
        Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) == 1,
        CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
        UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
    };
}
