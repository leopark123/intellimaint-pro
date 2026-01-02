using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDbExecutor _db;

    public AuditLogRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<long> CreateAsync(AuditLogEntry entry, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO audit_log (ts, user_id, user_name, action, resource_type, resource_id, details, ip_address)
VALUES (@Ts, @UserId, @UserName, @Action, @ResourceType, @ResourceId, @Details, @IpAddress);
SELECT last_insert_rowid();";

        var id = await _db.ExecuteScalarAsync<long>(sql, new
        {
            Ts = entry.Ts,
            UserId = entry.UserId,
            UserName = entry.UserName,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            Details = entry.Details,
            IpAddress = entry.IpAddress
        }, ct);

        return id;
    }

    public async Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> QueryAsync(AuditLogQuery query, CancellationToken ct)
    {
        var where = new List<string>();
        var parameters = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            where.Add("action = @Action");
            parameters["Action"] = query.Action;
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            where.Add("resource_type = @ResourceType");
            parameters["ResourceType"] = query.ResourceType;
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            where.Add("resource_id = @ResourceId");
            parameters["ResourceId"] = query.ResourceId;
        }

        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            where.Add("user_id = @UserId");
            parameters["UserId"] = query.UserId;
        }

        if (query.StartTs.HasValue)
        {
            where.Add("ts >= @StartTs");
            parameters["StartTs"] = query.StartTs.Value;
        }

        if (query.EndTs.HasValue)
        {
            where.Add("ts <= @EndTs");
            parameters["EndTs"] = query.EndTs.Value;
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        // Count
        var countSql = $"SELECT COUNT(*) FROM audit_log {whereClause};";
        var totalCount = await _db.ExecuteScalarAsync<int>(countSql, parameters, ct);

        // Query
        var limit = Math.Min(Math.Max(query.Limit, 1), 200);
        var offset = Math.Max(query.Offset, 0);

        var querySql = $@"
SELECT id, ts, user_id, user_name, action, resource_type, resource_id, details, ip_address
FROM audit_log {whereClause}
ORDER BY ts DESC, id DESC
LIMIT {limit} OFFSET {offset};";

        var items = await _db.QueryAsync(querySql, MapEntry, parameters, ct);
        return (items, totalCount);
    }

    public async Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken ct)
    {
        const string sql = "SELECT DISTINCT action FROM audit_log ORDER BY action;";
        var rows = await _db.QueryAsync(sql, r => r.GetString(0), null, ct);
        return rows;
    }

    public async Task<IReadOnlyList<string>> GetDistinctResourceTypesAsync(CancellationToken ct)
    {
        const string sql = "SELECT DISTINCT resource_type FROM audit_log ORDER BY resource_type;";
        var rows = await _db.QueryAsync(sql, r => r.GetString(0), null, ct);
        return rows;
    }

    private static AuditLogEntry MapEntry(SqliteDataReader reader)
    {
        return new AuditLogEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Ts = reader.GetInt64(reader.GetOrdinal("ts")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            UserName = reader.GetString(reader.GetOrdinal("user_name")),
            Action = reader.GetString(reader.GetOrdinal("action")),
            ResourceType = reader.GetString(reader.GetOrdinal("resource_type")),
            ResourceId = reader.IsDBNull(reader.GetOrdinal("resource_id")) ? null : reader.GetString(reader.GetOrdinal("resource_id")),
            Details = reader.IsDBNull(reader.GetOrdinal("details")) ? null : reader.GetString(reader.GetOrdinal("details")),
            IpAddress = reader.IsDBNull(reader.GetOrdinal("ip_address")) ? null : reader.GetString(reader.GetOrdinal("ip_address"))
        };
    }
}
