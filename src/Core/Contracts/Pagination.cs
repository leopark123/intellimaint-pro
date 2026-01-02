namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 分页Token (Keyset Pagination)
/// </summary>
public sealed record PageToken(long LastTs, long LastSeq)
{
    /// <summary>从字符串解析</summary>
    public static PageToken? Parse(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        
        var parts = token.Split('_');
        if (parts.Length != 2) return null;
        
        if (long.TryParse(parts[0], out var ts) && long.TryParse(parts[1], out var seq))
            return new PageToken(ts, seq);
        
        return null;
    }
    
    /// <summary>转为字符串</summary>
    public override string ToString() => $"{LastTs}_{LastSeq}";
}

/// <summary>
/// 排序方向
/// </summary>
public enum SortDirection
{
    Asc = 1,
    Desc = 2
}

/// <summary>
/// 历史查询规格
/// </summary>
public sealed record HistoryQuery
{
    public required string DeviceId { get; init; }
    public string? TagId { get; init; }
    public required long StartTs { get; init; }
    public required long EndTs { get; init; }
    public SortDirection Sort { get; init; } = SortDirection.Desc;
    public int Limit { get; init; } = 100;
    public PageToken? After { get; init; }
    public HistoryFilter? Filter { get; init; }
}

/// <summary>
/// 历史查询过滤条件
/// </summary>
public sealed record HistoryFilter
{
    public int? QualityEquals { get; init; }
    public int? QualityNotEquals { get; init; }
    public double? ValueGreaterThan { get; init; }
    public double? ValueLessThan { get; init; }
}

/// <summary>
/// 分页结果
/// </summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public PageToken? NextToken { get; init; }
    public bool HasMore { get; init; }
    public int TotalCount { get; init; }
    
    public static PagedResult<T> Empty() => new()
    {
        Items = Array.Empty<T>(),
        NextToken = null,
        HasMore = false,
        TotalCount = 0
    };
}
