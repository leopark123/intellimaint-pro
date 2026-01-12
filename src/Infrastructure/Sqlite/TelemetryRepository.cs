using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 遥测数据仓储实现
/// </summary>
public sealed class TelemetryRepository : ITelemetryRepository
{
    private readonly IDbExecutor _executor;
    private readonly ILogger<TelemetryRepository> _logger;
    
    public TelemetryRepository(IDbExecutor executor, ILogger<TelemetryRepository> logger)
    {
        _executor = executor;
        _logger = logger;
    }
    
    /// <summary>
    /// 批量追加数据点
    /// 使用事务确保原子性
    /// </summary>
    public async Task<int> AppendBatchAsync(IReadOnlyList<TelemetryPoint> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return 0;
        
        const string sql = @"
            INSERT OR IGNORE INTO telemetry (
                device_id, tag_id, ts, seq, value_type,
                bool_value, int8_value, uint8_value, int16_value, uint16_value,
                int32_value, uint32_value, int64_value, uint64_value,
                float32_value, float64_value, string_value, byte_array_value,
                quality, unit, source, protocol
            ) VALUES (
                @DeviceId, @TagId, @Ts, @Seq, @ValueType,
                @BoolValue, @Int8Value, @UInt8Value, @Int16Value, @UInt16Value,
                @Int32Value, @UInt32Value, @Int64Value, @UInt64Value,
                @Float32Value, @Float64Value, @StringValue, @ByteArrayValue,
                @Quality, @Unit, @Source, @Protocol
            )";
        
        var parametersList = batch.Select(p => new
        {
            p.DeviceId,
            p.TagId,
            p.Ts,
            p.Seq,
            ValueType = (int)p.ValueType,
            BoolValue = p.BoolValue.HasValue ? (p.BoolValue.Value ? 1 : 0) : (int?)null,
            Int8Value = (int?)p.Int8Value,
            UInt8Value = (int?)p.UInt8Value,
            Int16Value = (int?)p.Int16Value,
            UInt16Value = (int?)p.UInt16Value,
            p.Int32Value,
            UInt32Value = (long?)p.UInt32Value,
            p.Int64Value,
            UInt64Value = p.UInt64Value.HasValue ? (long?)unchecked((long)p.UInt64Value.Value) : null,
            p.Float32Value,
            p.Float64Value,
            p.StringValue,
            p.ByteArrayValue,
            p.Quality,
            p.Unit,
            p.Source,
            p.Protocol
        });
        
        try
        {
            var affected = await _executor.ExecuteBatchAsync(sql, parametersList, ct);
            _logger.LogDebug("Appended {Count} telemetry points", affected);
            return affected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 取消是正常的关闭流程，不记录为错误
            _logger.LogDebug("Append cancelled for {Count} telemetry points", batch.Count);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append {Count} telemetry points", batch.Count);
            throw;
        }
    }
    
    /// <summary>
    /// 查询历史数据（Keyset分页）
    /// </summary>
    public async Task<PagedResult<TelemetryPoint>> QueryAsync(HistoryQuery query, CancellationToken ct)
    {
        var sql = BuildQuerySql(query);
        var parameters = BuildQueryParameters(query);
        
        var items = await _executor.QueryAsync(sql, MapTelemetryPoint, parameters, ct);
        
        // 判断是否有更多
        var hasMore = items.Count > query.Limit;
        if (hasMore)
        {
            items = items.Take(query.Limit).ToList();
        }
        
        // 生成下一页 Token
        PageToken? nextToken = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextToken = new PageToken(last.Ts, last.Seq);
        }
        
        return new PagedResult<TelemetryPoint>
        {
            Items = items,
            NextToken = nextToken,
            HasMore = hasMore
        };
    }
    
    /// <summary>
    /// 删除指定时间之前的数据
    /// </summary>
    public async Task<int> DeleteBeforeAsync(long cutoffTs, CancellationToken ct)
    {
        const string sql = "DELETE FROM telemetry WHERE ts < @CutoffTs";
        var affected = await _executor.ExecuteNonQueryAsync(sql, new { CutoffTs = cutoffTs }, ct);
        
        _logger.LogInformation("Deleted {Count} telemetry points before {CutoffTs}", affected, cutoffTs);
        return affected;
    }
    
    /// <summary>
    /// 获取数据统计
    /// </summary>
    public async Task<TelemetryStats> GetStatsAsync(string? deviceId, CancellationToken ct)
    {
        string sql;
        object? parameters = null;
        
        if (string.IsNullOrEmpty(deviceId))
        {
            sql = @"
                SELECT 
                    COUNT(*) as TotalCount,
                    MIN(ts) as OldestTs,
                    MAX(ts) as NewestTs,
                    COUNT(DISTINCT device_id) as DeviceCount,
                    COUNT(DISTINCT tag_id) as TagCount
                FROM telemetry";
        }
        else
        {
            sql = @"
                SELECT 
                    COUNT(*) as TotalCount,
                    MIN(ts) as OldestTs,
                    MAX(ts) as NewestTs,
                    1 as DeviceCount,
                    COUNT(DISTINCT tag_id) as TagCount
                FROM telemetry
                WHERE device_id = @DeviceId";
            parameters = new { DeviceId = deviceId };
        }
        
        return await _executor.QuerySingleAsync(sql, reader => new TelemetryStats
        {
            TotalCount = reader.GetInt64(0),
            OldestTs = reader.IsDBNull(1) ? null : reader.GetInt64(1),
            NewestTs = reader.IsDBNull(2) ? null : reader.GetInt64(2),
            DeviceCount = reader.GetInt32(3),
            TagCount = reader.GetInt32(4)
        }, parameters, ct) ?? new TelemetryStats();
    }
    
    private static string BuildQuerySql(HistoryQuery query)
    {
        var orderDir = query.Sort == SortDirection.Asc ? "ASC" : "DESC";
        var compareOp = query.Sort == SortDirection.Asc ? ">" : "<";
        
        var sql = @"
            SELECT device_id, tag_id, ts, seq, value_type,
                   bool_value, int8_value, uint8_value, int16_value, uint16_value,
                   int32_value, uint32_value, int64_value, uint64_value,
                   float32_value, float64_value, string_value, byte_array_value,
                   quality, unit, source, protocol
            FROM telemetry
            WHERE device_id = @DeviceId
              AND ts BETWEEN @StartTs AND @EndTs";
        
        if (!string.IsNullOrEmpty(query.TagId))
        {
            sql += " AND tag_id = @TagId";
        }
        
        if (query.Filter != null)
        {
            if (query.Filter.QualityEquals.HasValue)
                sql += " AND quality = @QualityEquals";
            if (query.Filter.QualityNotEquals.HasValue)
                sql += " AND quality <> @QualityNotEquals";
        }
        
        // Keyset 分页条件
        if (query.After != null)
        {
            sql += $@"
              AND (ts {compareOp} @LastTs OR (ts = @LastTs AND seq {compareOp} @LastSeq))";
        }
        
        sql += $@"
            ORDER BY ts {orderDir}, seq {orderDir}
            LIMIT @LimitPlusOne";
        
        return sql;
    }
    
    private static object BuildQueryParameters(HistoryQuery query)
    {
        return new
        {
            query.DeviceId,
            query.TagId,
            query.StartTs,
            query.EndTs,
            LimitPlusOne = query.Limit + 1,  // 多取一条判断 HasMore
            LastTs = query.After?.LastTs,
            LastSeq = query.After?.LastSeq,
            query.Filter?.QualityEquals,
            query.Filter?.QualityNotEquals
        };
    }
    
    private static TelemetryPoint MapTelemetryPoint(SqliteDataReader reader)
    {
        var valueType = (TagValueType)reader.GetInt32(4);
        
        return new TelemetryPoint
        {
            DeviceId = reader.GetString(0),
            TagId = reader.GetString(1),
            Ts = reader.GetInt64(2),
            Seq = reader.GetInt64(3),
            ValueType = valueType,
            BoolValue = reader.IsDBNull(5) ? null : reader.GetInt32(5) != 0,
            Int8Value = reader.IsDBNull(6) ? null : (sbyte)reader.GetInt32(6),
            UInt8Value = reader.IsDBNull(7) ? null : (byte)reader.GetInt32(7),
            Int16Value = reader.IsDBNull(8) ? null : (short)reader.GetInt32(8),
            UInt16Value = reader.IsDBNull(9) ? null : (ushort)reader.GetInt32(9),
            Int32Value = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            UInt32Value = reader.IsDBNull(11) ? null : (uint)reader.GetInt64(11),
            Int64Value = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            UInt64Value = reader.IsDBNull(13) ? null : unchecked((ulong)reader.GetInt64(13)),
            Float32Value = reader.IsDBNull(14) ? null : reader.GetFloat(14),
            Float64Value = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            StringValue = reader.IsDBNull(16) ? null : reader.GetString(16),
            ByteArrayValue = reader.IsDBNull(17) ? null : (byte[])reader.GetValue(17),
            Quality = reader.GetInt32(18),
            Unit = reader.IsDBNull(19) ? null : reader.GetString(19),
            Source = reader.IsDBNull(20) ? "edge" : reader.GetString(20),
            Protocol = reader.IsDBNull(21) ? null : reader.GetString(21)
        };
    }

    // -------------------------------------------------------------------
    // Batch 7 新增：简化 Query / Latest / Tags / Aggregate
    // -------------------------------------------------------------------

    /// <summary>
    /// 查询历史数据（简化版）
    /// </summary>
    public async Task<IReadOnlyList<TelemetryPoint>> QuerySimpleAsync(
        string? deviceId,
        string? tagId,
        long? startTs,
        long? endTs,
        int limit,
        CancellationToken ct)
    {
        var (data, _) = await QueryWithCursorAsync(deviceId, tagId, startTs, endTs, limit, null, null, ct);
        return data;
    }
    
    /// <summary>
    /// v48: 游标分页查询
    /// v56.1: 无筛选条件时自动限制时间范围，避免全表扫描
    /// </summary>
    public async Task<(IReadOnlyList<TelemetryPoint> Data, bool HasMore)> QueryWithCursorAsync(
        string? deviceId,
        string? tagId,
        long? startTs,
        long? endTs,
        int limit,
        long? cursorTs,
        int? cursorSeq,
        CancellationToken ct)
    {
        var sql = new System.Text.StringBuilder(@"
            SELECT device_id, tag_id, ts, seq, value_type,
                   bool_value, int8_value, uint8_value, int16_value, uint16_value,
                   int32_value, uint32_value, int64_value, uint64_value,
                   float32_value, float64_value, string_value, byte_array_value,
                   quality, unit, source, protocol
            FROM telemetry
            WHERE 1=1
        ");

        var p = new Dictionary<string, object>();

        // v56.1: 无筛选条件时默认限制最近24小时，避免全表扫描
        var hasFilter = !string.IsNullOrWhiteSpace(deviceId) || !string.IsNullOrWhiteSpace(tagId);
        var effectiveStartTs = startTs;
        if (!hasFilter && !startTs.HasValue)
        {
            effectiveStartTs = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
            _logger.LogDebug("No filter specified, limiting to last 24 hours");
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql.Append(" AND device_id = @DeviceId");
            p["DeviceId"] = deviceId;
        }

        if (!string.IsNullOrWhiteSpace(tagId))
        {
            sql.Append(" AND tag_id = @TagId");
            p["TagId"] = tagId;
        }

        if (effectiveStartTs.HasValue)
        {
            sql.Append(" AND ts >= @StartTs");
            p["StartTs"] = effectiveStartTs.Value;
        }

        if (endTs.HasValue)
        {
            sql.Append(" AND ts <= @EndTs");
            p["EndTs"] = endTs.Value;
        }
        
        // v48: 游标分页 - 使用 (ts, seq) 复合游标
        if (cursorTs.HasValue)
        {
            sql.Append(" AND (ts < @CursorTs OR (ts = @CursorTs AND seq < @CursorSeq))");
            p["CursorTs"] = cursorTs.Value;
            p["CursorSeq"] = cursorSeq ?? 0;
        }

        var safeLimit = Math.Clamp(limit, 1, 10_000);
        // 多取一条判断是否还有更多
        sql.Append(" ORDER BY ts DESC, seq DESC LIMIT @Limit");
        p["Limit"] = safeLimit + 1;

        var rows = await _executor.QueryAsync(sql.ToString(), MapTelemetryPoint, p, ct);
        
        // 判断是否有更多数据
        var hasMore = rows.Count > safeLimit;
        var data = hasMore ? rows.Take(safeLimit).ToList() : rows;
        
        return (data, hasMore);
    }

    /// <summary>
    /// 获取最新值
    /// v56: 优化查询性能 - 限制时间范围避免全表扫描
    /// </summary>
    public async Task<IReadOnlyList<TelemetryPoint>> GetLatestAsync(
        string? deviceId,
        string? tagId,
        CancellationToken ct)
    {
        // v56: 性能优化 - 使用 MAX 子查询替代 ROW_NUMBER 窗口函数
        // 性能提升：从 30秒+ 降到 <100ms
        var cutoffTs = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        
        var sql = new System.Text.StringBuilder(@"
            SELECT t.device_id, t.tag_id, t.ts, t.seq, t.value_type,
                   t.bool_value, t.int8_value, t.uint8_value, t.int16_value, t.uint16_value,
                   t.int32_value, t.uint32_value, t.int64_value, t.uint64_value,
                   t.float32_value, t.float64_value, t.string_value, t.byte_array_value,
                   t.quality, t.unit, t.source, t.protocol
            FROM telemetry t
            INNER JOIN (
                SELECT device_id, tag_id, MAX(ts) as max_ts
                FROM telemetry
                WHERE ts >= @CutoffTs
        ");

        var p = new Dictionary<string, object>
        {
            ["CutoffTs"] = cutoffTs
        };

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            sql.Append(" AND device_id = @DeviceId");
            p["DeviceId"] = deviceId;
        }

        if (!string.IsNullOrWhiteSpace(tagId))
        {
            sql.Append(" AND tag_id = @TagId");
            p["TagId"] = tagId;
        }

        sql.Append(@"
                GROUP BY device_id, tag_id
            ) latest ON t.device_id = latest.device_id 
                    AND t.tag_id = latest.tag_id 
                    AND t.ts = latest.max_ts
            ORDER BY t.device_id, t.tag_id
        ");

        var rows = await _executor.QueryAsync(sql.ToString(), MapTelemetryPoint, p, ct);
        return rows;
    }

    /// <summary>
    /// 获取所有已知的 Tag 列表
    /// v56: 优化 - 从 tag 表获取，避免扫描百万级 telemetry 表
    /// </summary>
    public async Task<IReadOnlyList<TagInfo>> GetTagsAsync(CancellationToken ct)
    {
        // 策略：从 tag 表获取标签列表，只查最近数据的最后时间
        var cutoffTs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        
        const string sql = @"
            SELECT 
                t.device_id,
                t.tag_id,
                CASE t.data_type 
                    WHEN 'Float32' THEN 10
                    WHEN 'Float64' THEN 11
                    WHEN 'Int32' THEN 6
                    WHEN 'Int16' THEN 4
                    WHEN 'Bool' THEN 1
                    WHEN 'String' THEN 12
                    ELSE 10
                END as value_type,
                t.unit,
                (SELECT MAX(ts) FROM telemetry tel 
                 WHERE tel.device_id = t.device_id AND tel.tag_id = t.tag_id 
                 AND tel.ts >= @CutoffTs) as last_ts,
                0 as point_count
            FROM tag t
            WHERE t.enabled = 1
            ORDER BY t.device_id, t.tag_id";

        var rows = await _executor.QueryAsync(sql, reader =>
        {
            return new TagInfo
            {
                DeviceId = reader.GetString(0),
                TagId = reader.GetString(1),
                ValueType = (TagValueType)reader.GetInt32(2),
                Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastTs = reader.IsDBNull(4) ? null : reader.GetInt64(4),
                PointCount = 0 // v56: 不再统计总数，太慢
            };
        }, new { CutoffTs = cutoffTs }, ct);

        return rows;
    }

    /// <summary>
    /// 聚合查询
    /// </summary>
    public async Task<IReadOnlyList<AggregateResult>> AggregateAsync(
        string deviceId,
        string tagId,
        long startTs,
        long endTs,
        int intervalMs,
        AggregateFunction func,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("deviceId is required", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(tagId)) throw new ArgumentException("tagId is required", nameof(tagId));
        if (endTs <= startTs) throw new ArgumentException("endTs must be greater than startTs");
        if (intervalMs <= 0) throw new ArgumentException("intervalMs must be > 0");

        const string numericExpr = @"
            CAST(
                COALESCE(
                    float64_value,
                    float32_value,
                    int64_value,
                    uint64_value,
                    int32_value,
                    uint32_value,
                    int16_value,
                    uint16_value,
                    int8_value,
                    uint8_value,
                    bool_value
                ) AS REAL
            )";

        string aggExpr = func switch
        {
            AggregateFunction.Avg => $"AVG({numericExpr})",
            AggregateFunction.Min => $"MIN({numericExpr})",
            AggregateFunction.Max => $"MAX({numericExpr})",
            AggregateFunction.Sum => $"SUM({numericExpr})",
            AggregateFunction.Count => "COUNT(*)",
            AggregateFunction.First => $"MIN({numericExpr})",
            AggregateFunction.Last => $"MAX({numericExpr})",
            _ => $"AVG({numericExpr})"
        };

        var sql = $@"
            SELECT 
                (ts / @IntervalMs) * @IntervalMs AS bucket_ts,
                {aggExpr} AS value,
                COUNT(*) AS count
            FROM telemetry
            WHERE device_id = @DeviceId
              AND tag_id = @TagId
              AND ts >= @StartTs
              AND ts <  @EndTs
            GROUP BY bucket_ts
            ORDER BY bucket_ts";

        var p = new Dictionary<string, object>
        {
            ["DeviceId"] = deviceId,
            ["TagId"] = tagId,
            ["StartTs"] = startTs,
            ["EndTs"] = endTs,
            ["IntervalMs"] = intervalMs
        };

        var rows = await _executor.QueryAsync(sql, reader =>
        {
            var bucketTs = reader.GetInt64(0);
            double value = reader.IsDBNull(1) ? 0d : reader.GetDouble(1);
            var count = reader.GetInt32(2);

            return new AggregateResult
            {
                Ts = bucketTs,
                Value = value,
                Count = count
            };
        }, p, ct);

        return rows;
    }
}
