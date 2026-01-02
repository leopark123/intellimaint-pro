using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 配置变更监视器配置
/// </summary>
public sealed class ConfigWatcherOptions
{
    public const string SectionName = "ConfigWatcher";
    
    /// <summary>检查间隔（毫秒），默认 1000ms</summary>
    public int IntervalMs { get; init; } = 1000;
    
    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// 配置变更监视器
/// 定期检查 config.revision，检测到变化时触发热重载
/// </summary>
public sealed class ConfigChangeWatcher : BackgroundService
{
    private readonly IDbConfigProvider _configProvider;
    private readonly IConfigRevisionProvider _revisionProvider;
    private readonly ILogger<ConfigChangeWatcher> _logger;
    private readonly int _intervalMs;
    private readonly bool _enabled;
    
    private long _lastRevision;

    public ConfigChangeWatcher(
        IDbConfigProvider configProvider,
        IConfigRevisionProvider revisionProvider,
        IOptions<ConfigWatcherOptions> options,
        ILogger<ConfigChangeWatcher> logger)
    {
        _configProvider = configProvider;
        _revisionProvider = revisionProvider;
        _logger = logger;
        _intervalMs = options.Value.IntervalMs;
        _enabled = options.Value.Enabled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("ConfigChangeWatcher is disabled");
            return;
        }
        
        _logger.LogInformation("ConfigChangeWatcher started, checking revision every {Interval}ms", _intervalMs);
        
        // 初始化基线
        await InitializeBaselineAsync(stoppingToken);
        
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_intervalMs));
        
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckForChangesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("ConfigChangeWatcher stopping...");
        }
    }

    private async Task InitializeBaselineAsync(CancellationToken ct)
    {
        try
        {
            _lastRevision = await _revisionProvider.GetRevisionAsync(ct);
            _logger.LogDebug("ConfigChangeWatcher baseline revision: {Revision}", _lastRevision);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize config revision baseline");
            _lastRevision = 0;
        }
    }

    private async Task CheckForChangesAsync(CancellationToken ct)
    {
        try
        {
            var currentRevision = await _revisionProvider.GetRevisionAsync(ct);
            
            if (currentRevision > _lastRevision)
            {
                _logger.LogInformation(
                    "Configuration revision changed: {OldRevision} -> {NewRevision}",
                    _lastRevision, currentRevision);
                
                _lastRevision = currentRevision;
                
                // 触发热重载
                _configProvider.NotifyConfigChanged();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking config revision");
        }
    }
}
