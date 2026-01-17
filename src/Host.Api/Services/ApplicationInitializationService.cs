using IntelliMaint.Application.Services;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v63: 应用初始化服务
/// 在应用启动时异步初始化需要预热的组件
/// </summary>
public sealed class ApplicationInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationInitializationService> _logger;

    public ApplicationInitializationService(
        IServiceProvider serviceProvider,
        ILogger<ApplicationInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Application initialization starting...");

        try
        {
            await InitializeTagImportanceMatcherAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application initialization failed");
            // 不抛出异常，允许应用继续启动
            // 组件会使用默认值并记录警告
        }

        _logger.LogInformation("Application initialization completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeTagImportanceMatcherAsync(CancellationToken ct)
    {
        var matcher = _serviceProvider.GetRequiredService<TagImportanceMatcher>();
        await matcher.InitializeAsync(ct);
        _logger.LogDebug("TagImportanceMatcher initialized");
    }
}
