using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 配置版本提供者
/// 使用 system_setting 表存储 config.revision，用于配置变更检测
/// </summary>
public sealed class ConfigRevisionProvider : IConfigRevisionProvider
{
    private const string RevisionKey = "config.revision";
    
    private readonly ISystemSettingRepository _settingRepo;
    private readonly ILogger<ConfigRevisionProvider> _logger;

    public ConfigRevisionProvider(
        ISystemSettingRepository settingRepo,
        ILogger<ConfigRevisionProvider> logger)
    {
        _settingRepo = settingRepo;
        _logger = logger;
    }

    public async Task<long> GetRevisionAsync(CancellationToken ct)
    {
        var value = await _settingRepo.GetAsync(RevisionKey, ct);
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        
        return long.TryParse(value, out var revision) ? revision : 0;
    }

    public async Task IncrementRevisionAsync(CancellationToken ct)
    {
        var current = await GetRevisionAsync(ct);
        var newRevision = current + 1;
        
        await _settingRepo.SetAsync(RevisionKey, newRevision.ToString(), ct);
        
        _logger.LogDebug("Config revision incremented: {Old} -> {New}", current, newRevision);
    }
}
