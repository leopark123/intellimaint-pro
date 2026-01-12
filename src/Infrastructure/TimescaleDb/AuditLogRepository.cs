using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB AuditLog repository implementation
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(INpgsqlConnectionFactory factory, ILogger<AuditLogRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO audit_log (ts, user_id, user_name, action, resource_type, resource_id, details, ip_address)
            VALUES (@Ts, @UserId, @UserName, @Action, @ResourceType, @ResourceId, @Details, @IpAddress)
            RETURNING id";

        using var conn = _factory.CreateConnection();
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            entry.Ts,
            entry.UserId,
            entry.UserName,
            entry.Action,
            entry.ResourceType,
            entry.ResourceId,
            entry.Details,
            entry.IpAddress
        }, cancellationToken: ct));

        return id;
    }

    public async Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> QueryAsync(AuditLogQuery query, CancellationToken ct)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            conditions.Add("action = @Action");
            p.Add("Action", query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            conditions.Add("resource_type = @ResourceType");
            p.Add("ResourceType", query.ResourceType);
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            conditions.Add("resource_id = @ResourceId");
            p.Add("ResourceId", query.ResourceId);
        }

        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            conditions.Add("user_id = @UserId");
            p.Add("UserId", query.UserId);
        }

        if (query.StartTs.HasValue)
        {
            conditions.Add("ts >= @StartTs");
            p.Add("StartTs", query.StartTs.Value);
        }

        if (query.EndTs.HasValue)
        {
            conditions.Add("ts <= @EndTs");
            p.Add("EndTs", query.EndTs.Value);
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        // Use PaginationHelper-like clamping
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);

        // Use window function to get total count in a single query
        var sql = $@"
            SELECT id, ts, user_id, user_name, action, resource_type, resource_id, details, ip_address,
                   COUNT(*) OVER() as total_count
            FROM audit_log {whereClause}
            ORDER BY ts DESC, id DESC
            LIMIT {limit} OFFSET {offset}";

        using var conn = _factory.CreateConnection();
        var rows = (await conn.QueryAsync<AuditLogRowWithTotal>(
            new CommandDefinition(sql, p, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return (Array.Empty<AuditLogEntry>(), 0);
        }

        var totalCount = (int)rows[0].total_count;
        var items = rows.Select(r => new AuditLogEntry
        {
            Id = r.id,
            Ts = r.ts,
            UserId = r.user_id,
            UserName = r.user_name,
            Action = r.action,
            ResourceType = r.resource_type,
            ResourceId = r.resource_id,
            Details = r.details,
            IpAddress = r.ip_address
        }).ToList();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct)
    {
        const string sql = "SELECT DISTINCT action FROM audit_log ORDER BY action";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<string>> GetDistinctResourceTypesAsync(CancellationToken ct)
    {
        const string sql = "SELECT DISTINCT resource_type FROM audit_log ORDER BY resource_type";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<string>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM audit_log WHERE ts < @CutoffTs";

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteAsync(new CommandDefinition(sql, new { CutoffTs = cutoffTs }, cancellationToken: ct));
    }

    private sealed class AuditLogRowWithTotal
    {
        public long id { get; set; }
        public long ts { get; set; }
        public string user_id { get; set; } = "";
        public string user_name { get; set; } = "";
        public string action { get; set; } = "";
        public string resource_type { get; set; } = "";
        public string? resource_id { get; set; }
        public string? details { get; set; }
        public string? ip_address { get; set; }
        public long total_count { get; set; }
    }
}
