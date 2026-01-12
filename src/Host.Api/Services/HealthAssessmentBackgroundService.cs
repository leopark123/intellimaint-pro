using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v60: 后台健康评估服务
/// 每60秒执行一次全设备健康评估，保存快照到数据库
/// </summary>
public sealed class HealthAssessmentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthAssessmentBackgroundService> _logger;

    // 评估间隔（秒）
    private const int IntervalSeconds = 60;

    // 快照保留天数
    private const int RetentionDays = 7;

    public HealthAssessmentBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<HealthAssessmentBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HealthAssessmentBackgroundService started, interval={Interval}s", IntervalSeconds);

        // 启动延迟，等待其他服务就绪
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAssessmentCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health assessment cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("HealthAssessmentBackgroundService stopped");
    }

    private async Task RunAssessmentCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var healthService = scope.ServiceProvider.GetRequiredService<HealthAssessmentService>();
        var snapshotRepo = scope.ServiceProvider.GetRequiredService<IDeviceHealthSnapshotRepository>();

        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. 执行全设备健康评估
        var scores = await healthService.AssessAllDevicesAsync(ct: ct);
        if (scores.Count == 0)
        {
            _logger.LogDebug("No devices to assess");
            return;
        }

        // 2. 转换为快照并保存
        var snapshots = scores.Select(s => new DeviceHealthSnapshot
        {
            DeviceId = s.DeviceId,
            Timestamp = nowUtc,
            Index = s.Index,
            Level = s.Level,
            DeviationScore = s.DeviationScore,
            TrendScore = s.TrendScore,
            StabilityScore = s.StabilityScore,
            AlarmScore = s.AlarmScore
        });

        await snapshotRepo.SaveBatchAsync(snapshots, ct);

        _logger.LogInformation("Health assessment completed: {Count} devices evaluated", scores.Count);

        // 3. 清理过期数据（每次评估都清理一次，轻量操作）
        var cutoffTs = DateTimeOffset.UtcNow.AddDays(-RetentionDays).ToUnixTimeMilliseconds();
        var deleted = await snapshotRepo.DeleteBeforeAsync(cutoffTs, ct);
        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired health snapshots", deleted);
        }
    }
}
