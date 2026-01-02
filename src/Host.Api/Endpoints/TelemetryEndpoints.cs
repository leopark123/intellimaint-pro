using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Mvc;

namespace IntelliMaint.Host.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/telemetry")
            .WithTags("Telemetry")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);  // 所有已认证用户可访问

        group.MapGet("/query", QueryAsync)
            .WithName("QueryTelemetry")
            .WithDescription("查询历史遥测数据");

        group.MapGet("/latest", GetLatestAsync)
            .WithName("GetLatestTelemetry")
            .WithDescription("获取最新遥测值");

        group.MapGet("/tags", GetTagsAsync)
            .WithName("GetTags")
            .WithDescription("获取所有已知标签");

        group.MapGet("/aggregate", AggregateAsync)
            .WithName("AggregateTelemetry")
            .WithDescription("聚合查询");
            
        // 诊断端点
        group.MapGet("/debug", DebugAsync)
            .WithName("DebugTelemetry")
            .WithDescription("诊断信息");
    }
    
    private static async Task<IResult> DebugAsync(
        [FromServices] ISqliteConnectionFactory connFactory,
        [FromServices] ITelemetryRepository repo,
        CancellationToken ct)
    {
        var dbPath = connFactory.DatabasePath;
        var fullPath = Path.GetFullPath(dbPath);
        var exists = File.Exists(fullPath);
        
        long rowCount = 0;
        string? error = null;
        
        if (exists)
        {
            try
            {
                var stats = await repo.GetStatsAsync(null, ct);
                rowCount = stats.TotalCount;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
        
        return Results.Ok(new
        {
            configuredPath = dbPath,
            fullPath = fullPath,
            fileExists = exists,
            rowCount = rowCount,
            error = error,
            currentDirectory = Directory.GetCurrentDirectory()
        });
    }

    private static async Task<IResult> QueryAsync(
        [FromServices] ITelemetryRepository repo,
        [AsParameters] TelemetryQueryRequest req,
        CancellationToken ct)
    {
        var limit = NormalizeLimit(req.Limit);
        
        // v48: 支持游标分页
        var (points, hasMore) = await repo.QueryWithCursorAsync(
            req.DeviceId, req.TagId, req.StartTs, req.EndTs, limit, 
            req.CursorTs, req.CursorSeq, ct);

        var data = points.Select(ToApiPoint).ToList();
        
        // 计算下一页游标
        string? nextCursor = null;
        if (hasMore && data.Count > 0)
        {
            var last = data[^1];
            nextCursor = $"{last.Ts}:{last.Seq}";
        }

        return Results.Ok(new PagedApiResponse<IReadOnlyList<TelemetryDataPoint>>
        {
            Success = true,
            Data = data,
            Count = data.Count,
            HasMore = hasMore,
            NextCursor = nextCursor
        });
    }

    private static async Task<IResult> GetLatestAsync(
        [FromServices] ITelemetryRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] string? tagId,
        CancellationToken ct)
    {
        var points = await repo.GetLatestAsync(deviceId, tagId, ct);
        var data = points.Select(ToApiPoint).ToList();

        return Results.Ok(new ApiResponse<IReadOnlyList<TelemetryDataPoint>>
        {
            Success = true,
            Data = data
        });
    }

    private static async Task<IResult> GetTagsAsync(
        [FromServices] ITelemetryRepository repo,
        CancellationToken ct)
    {
        var tags = await repo.GetTagsAsync(ct);

        var data = tags.Select(t => new
        {
            deviceId = t.DeviceId,
            tagId = t.TagId,
            valueType = t.ValueType.ToString(),
            unit = t.Unit,
            lastTs = t.LastTs,
            pointCount = t.PointCount
        }).ToList();

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>
        {
            Success = true,
            Data = data
        });
    }

    private static async Task<IResult> AggregateAsync(
        [FromServices] ITelemetryRepository repo,
        [AsParameters] AggregateQueryRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceId) ||
            string.IsNullOrWhiteSpace(req.TagId))
        {
            return Results.BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = "deviceId and tagId are required"
            });
        }

        if (req.EndTs <= req.StartTs)
        {
            return Results.BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = "endTs must be greater than startTs"
            });
        }

        var intervalMs = req.IntervalMs <= 0 ? 60_000 : req.IntervalMs;

        if (!TryParseAggregateFunction(req.Function, out var func))
        {
            return Results.BadRequest(new ApiResponse<object>
            {
                Success = false,
                Error = "function must be one of: avg|min|max|sum|count"
            });
        }

        var result = await repo.AggregateAsync(req.DeviceId, req.TagId, req.StartTs, req.EndTs, intervalMs, func, ct);

        return Results.Ok(new ApiResponse<IReadOnlyList<AggregateResult>>
        {
            Success = true,
            Data = result
        });
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0) return 1000;
        return Math.Min(limit, 10_000);
    }

    private static bool TryParseAggregateFunction(string? func, out AggregateFunction result)
    {
        var f = (func ?? "avg").Trim().ToLowerInvariant();
        result = f switch
        {
            "avg" => AggregateFunction.Avg,
            "min" => AggregateFunction.Min,
            "max" => AggregateFunction.Max,
            "sum" => AggregateFunction.Sum,
            "count" => AggregateFunction.Count,
            _ => AggregateFunction.Avg
        };

        return f is "avg" or "min" or "max" or "sum" or "count";
    }

    private static TelemetryDataPoint ToApiPoint(TelemetryPoint p)
    {
        return new TelemetryDataPoint
        {
            DeviceId = p.DeviceId,
            TagId = p.TagId,
            Ts = p.Ts,
            Seq = p.Seq,  // v48: 添加序号用于游标分页
            Value = ExtractValue(p),
            ValueType = p.ValueType.ToString(),
            Quality = p.Quality,
            Unit = p.Unit
        };
    }

    public static object? ExtractValue(TelemetryPoint p)
    {
        return p.ValueType switch
        {
            TagValueType.Bool => p.BoolValue,
            TagValueType.Int8 => p.Int8Value,
            TagValueType.UInt8 => p.UInt8Value,
            TagValueType.Int16 => p.Int16Value,
            TagValueType.UInt16 => p.UInt16Value,
            TagValueType.Int32 => p.Int32Value,
            TagValueType.UInt32 => p.UInt32Value,
            TagValueType.Int64 => p.Int64Value,
            TagValueType.UInt64 => p.UInt64Value,
            TagValueType.Float32 => p.Float32Value,
            TagValueType.Float64 => p.Float64Value,
            TagValueType.String => p.StringValue,
            TagValueType.ByteArray => p.ByteArrayValue != null ? Convert.ToBase64String(p.ByteArrayValue) : null,
            TagValueType.DateTime => p.Int64Value,
            _ => null
        };
    }
}
