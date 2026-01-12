using IntelliMaint.Application.Services;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v62: 动态基线后台更新服务
/// 定期执行基线增量更新
/// </summary>
public sealed class DynamicBaselineBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HealthAssessmentOptions _options;
    private readonly ILogger<DynamicBaselineBackgroundService> _logger;

    public DynamicBaselineBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<HealthAssessmentOptions> options,
        ILogger<DynamicBaselineBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = _options.DynamicBaseline;

        if (!config.Enabled)
        {
            _logger.LogInformation("Dynamic baseline update is disabled");
            return;
        }

        _logger.LogInformation(
            "Dynamic baseline service started. Update interval: {Hours} hours",
            config.UpdateIntervalHours);

        // 延迟启动，等待系统初始化完成
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateBaselinesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dynamic baseline update failed");
            }

            // 等待下一个更新周期
            var delayHours = config.UpdateIntervalHours;
            await Task.Delay(TimeSpan.FromHours(delayHours), stoppingToken);
        }

        _logger.LogInformation("Dynamic baseline service stopped");
    }

    private async Task UpdateBaselinesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting dynamic baseline update...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var scope = _serviceProvider.CreateScope();
        var baselineService = scope.ServiceProvider.GetRequiredService<DynamicBaselineService>();

        await baselineService.UpdateAllBaselinesAsync(ct);

        sw.Stop();
        _logger.LogInformation("Dynamic baseline update completed in {Elapsed}ms", sw.ElapsedMilliseconds);
    }
}
