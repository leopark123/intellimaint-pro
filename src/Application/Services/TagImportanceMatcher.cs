using System.Text.RegularExpressions;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v61: 标签重要性匹配服务实现
/// 支持通配符模式匹配，带内存缓存
/// </summary>
public sealed class TagImportanceMatcher : ITagImportanceMatcher
{
    private readonly ITagImportanceRepository _repository;
    private readonly ILogger<TagImportanceMatcher> _logger;
    private readonly TagImportance _defaultImportance;

    // 缓存：编译后的正则表达式配置
    private List<(Regex Regex, TagImportance Importance)> _compiledPatterns = new();
    private readonly object _lock = new();
    private volatile bool _initialized;
    private volatile bool _warnedNotInitialized;

    public TagImportanceMatcher(
        ITagImportanceRepository repository,
        IOptions<HealthAssessmentOptions> options,
        ILogger<TagImportanceMatcher> logger)
    {
        _repository = repository;
        _logger = logger;
        _defaultImportance = options.Value.DefaultTagImportance;
    }

    /// <inheritdoc />
    public TagImportance GetImportance(string tagId)
    {
        WarnIfNotInitialized();

        lock (_lock)
        {
            // 按优先级顺序匹配（列表已按优先级降序排列）
            foreach (var (regex, importance) in _compiledPatterns)
            {
                if (regex.IsMatch(tagId))
                {
                    return importance;
                }
            }
        }

        return _defaultImportance;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TagImportance> GetImportances(IEnumerable<string> tagIds)
    {
        WarnIfNotInitialized();

        var result = new Dictionary<string, TagImportance>();

        lock (_lock)
        {
            foreach (var tagId in tagIds)
            {
                var importance = _defaultImportance;

                foreach (var (regex, imp) in _compiledPatterns)
                {
                    if (regex.IsMatch(tagId))
                    {
                        importance = imp;
                        break;
                    }
                }

                result[tagId] = importance;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken ct)
    {
        var configs = await _repository.ListEnabledAsync(ct);

        var newPatterns = new List<(Regex, TagImportance)>();

        foreach (var config in configs)
        {
            try
            {
                var regex = WildcardToRegex(config.Pattern);
                newPatterns.Add((regex, config.Importance));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid pattern in tag importance config {Id}: {Pattern}",
                    config.Id, config.Pattern);
            }
        }

        lock (_lock)
        {
            _compiledPatterns = newPatterns;
            _initialized = true;
        }

        _logger.LogInformation("Refreshed tag importance patterns: {Count} rules loaded", newPatterns.Count);
    }

    /// <summary>
    /// 异步初始化（推荐在应用启动时调用）
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await RefreshAsync(ct);
        _logger.LogInformation("Tag importance matcher initialized asynchronously");
    }

    /// <summary>
    /// 检查是否已初始化（不阻塞）
    /// 如果未初始化，记录警告并使用默认值（仅警告一次）
    /// </summary>
    private void WarnIfNotInitialized()
    {
        if (!_initialized && !_warnedNotInitialized)
        {
            _warnedNotInitialized = true;
            _logger.LogWarning(
                "TagImportanceMatcher not initialized, using default importance. " +
                "Ensure InitializeAsync() is called at startup.");
        }
    }

    /// <summary>
    /// 将通配符模式转换为正则表达式
    /// 支持 * (任意字符) 和 ? (单个字符)
    /// </summary>
    private static Regex WildcardToRegex(string pattern)
    {
        // 转义正则特殊字符，然后替换通配符
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        // 添加锚点确保完整匹配
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

/// <summary>
/// v61: 标签重要性匹配服务（无仓储依赖版本，用于单元测试或简单场景）
/// </summary>
public sealed class InMemoryTagImportanceMatcher : ITagImportanceMatcher
{
    private readonly List<(Regex Regex, TagImportance Importance)> _patterns = new();
    private readonly TagImportance _defaultImportance;

    public InMemoryTagImportanceMatcher(
        IEnumerable<TagImportanceConfig>? configs = null,
        TagImportance defaultImportance = TagImportance.Minor)
    {
        _defaultImportance = defaultImportance;

        if (configs != null)
        {
            foreach (var config in configs.OrderByDescending(c => c.Priority))
            {
                if (config.Enabled)
                {
                    var regex = WildcardToRegex(config.Pattern);
                    _patterns.Add((regex, config.Importance));
                }
            }
        }
    }

    public TagImportance GetImportance(string tagId)
    {
        foreach (var (regex, importance) in _patterns)
        {
            if (regex.IsMatch(tagId))
                return importance;
        }
        return _defaultImportance;
    }

    public IReadOnlyDictionary<string, TagImportance> GetImportances(IEnumerable<string> tagIds)
    {
        return tagIds.ToDictionary(t => t, GetImportance);
    }

    public Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
