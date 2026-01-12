namespace IntelliMaint.Core.Contracts;

/// <summary>
/// P2: 分页帮助类 - 提取公共分页逻辑
/// P1: 使用 SystemConstants 消除魔法数字
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// 默认每页大小（引用 SystemConstants）
    /// </summary>
    public const int DefaultPageSize = SystemConstants.Query.DefaultLimit;

    /// <summary>
    /// 最大每页大小（引用 SystemConstants）
    /// </summary>
    public const int MaxPageSize = SystemConstants.Query.MaxLimit;

    /// <summary>
    /// 规范化分页参数
    /// </summary>
    /// <param name="page">页码（1-based）</param>
    /// <param name="pageSize">每页大小</param>
    /// <param name="maxPageSize">最大每页大小，默认1000</param>
    /// <returns>规范化后的 (offset, limit)</returns>
    public static (int Offset, int Limit) Normalize(int? page, int? pageSize, int maxPageSize = MaxPageSize)
    {
        var limit = ClampLimit(pageSize ?? DefaultPageSize, 1, maxPageSize);
        var offset = Math.Max(0, ((page ?? 1) - 1) * limit);
        return (offset, limit);
    }

    /// <summary>
    /// 规范化 limit（确保在合理范围内）
    /// </summary>
    public static int ClampLimit(int limit, int min = 1, int max = MaxPageSize)
    {
        return Math.Min(Math.Max(limit, min), max);
    }

    /// <summary>
    /// 规范化 offset（确保非负）
    /// </summary>
    public static int ClampOffset(int offset)
    {
        return Math.Max(0, offset);
    }

    /// <summary>
    /// 计算是否有更多数据
    /// </summary>
    /// <param name="fetchedCount">实际获取的数量</param>
    /// <param name="requestedLimit">请求的 limit</param>
    public static bool HasMore(int fetchedCount, int requestedLimit)
    {
        return fetchedCount > requestedLimit;
    }

    /// <summary>
    /// 创建分页结果
    /// </summary>
    public static PagedResult<T> CreateResult<T>(
        IReadOnlyList<T> items,
        int totalCount,
        bool hasMore = false,
        PageToken? nextToken = null)
    {
        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            HasMore = hasMore,
            NextToken = nextToken
        };
    }

    /// <summary>
    /// 从 "limit + 1" 查询策略创建分页结果
    /// </summary>
    /// <param name="items">查询结果（可能包含多余的一条）</param>
    /// <param name="requestedLimit">请求的 limit</param>
    /// <param name="totalCount">总数（可选）</param>
    /// <param name="createToken">创建下一页 Token 的委托</param>
    public static PagedResult<T> CreateFromOverfetch<T>(
        List<T> items,
        int requestedLimit,
        int totalCount = -1,
        Func<T, PageToken>? createToken = null)
    {
        var hasMore = items.Count > requestedLimit;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var nextToken = hasMore && items.Count > 0 && createToken != null
            ? createToken(items[^1])
            : null;

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount >= 0 ? totalCount : items.Count,
            HasMore = hasMore,
            NextToken = nextToken
        };
    }
}
