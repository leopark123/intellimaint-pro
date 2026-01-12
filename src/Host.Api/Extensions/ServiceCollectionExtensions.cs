using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Hubs;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace IntelliMaint.Host.Api.Extensions;

/// <summary>
/// P2: 服务注册扩展方法 - 拆分 Program.cs 配置
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加应用层服务
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 审计服务
        services.AddHttpContextAccessor();
        services.AddScoped<AuditService>();

        // Token 黑名单服务（用于立即失效 Token）
        services.AddSingleton<TokenBlacklistService>();

        // 认证服务
        services.AddSingleton<ITokenService, JwtService>();
        services.AddScoped<IAuthService, AuthService>();

        // 用户服务
        services.AddScoped<IUserService, UserService>();

        // 告警服务
        services.AddScoped<IAlarmService, AlarmService>();

        // 健康评估服务
        services.AddSingleton<IFeatureExtractor, FeatureExtractor>();
        services.AddSingleton<IHealthScoreCalculator, HealthScoreCalculator>();
        services.AddSingleton<HealthAssessmentService>();

        // v61: 标签重要性匹配服务
        services.AddSingleton<ITagImportanceMatcher, TagImportanceMatcher>();

        // v62: 多标签关联分析服务
        services.AddSingleton<ICorrelationAnalyzer, CorrelationAnalyzer>();

        // v62: 动态基线服务
        services.AddScoped<DynamicBaselineService>();

        // v62: 多尺度评估服务
        services.AddSingleton<IMultiScaleAssessmentService, MultiScaleAssessmentService>();

        // v63: 趋势预测服务
        services.AddSingleton<ITrendPredictionService, TrendPredictionService>();

        // v63: 劣化检测服务
        services.AddSingleton<IDegradationDetectionService, DegradationDetectionService>();

        // v63: RUL 预测服务
        services.AddSingleton<IRulPredictionService, RulPredictionService>();

        // 周期分析服务
        services.AddSingleton<ICycleAnalysisService, CycleAnalysisService>();
        services.AddSingleton<IBaselineLearningService, BaselineLearningService>();

        // v64: 电机故障预测服务
        services.AddSingleton<MotorFftAnalyzer>();
        services.AddSingleton<OperationModeDetector>();
        services.AddSingleton<MotorBaselineLearningService>();
        services.AddSingleton<MotorFaultDetectionService>();

        // v65: Edge 配置变更通知服务
        services.AddSingleton<EdgeNotificationService>();
        services.AddSingleton<IEdgeNotificationService>(sp => sp.GetRequiredService<EdgeNotificationService>());

        // MediatR
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<IntelliMaint.Application.Events.AlarmCreatedEvent>());

        return services;
    }

    /// <summary>
    /// 添加 JWT 认证
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<JwtService>();

        // 优先级: 环境变量 > appsettings.json
        var jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException(
                "JWT_SECRET_KEY environment variable or Jwt:SecretKey config is required");

        if (jwtSecretKey.Length < SystemConstants.Auth.MinSecretKeyLength)
        {
            throw new InvalidOperationException(
                $"JWT secret key must be at least {SystemConstants.Auth.MinSecretKeyLength} characters");
        }

        Log.Information("JWT configured. SecretKey source: {Source}",
            Environment.GetEnvironmentVariable("JWT_SECRET_KEY") != null
                ? "Environment Variable"
                : "appsettings.json");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"] ?? "IntelliMaint",
                    ValidAudience = configuration["Jwt:Audience"] ?? "IntelliMaint",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                // SignalR JWT 认证配置 + Token 黑名单检查
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        // 检查 Token 是否在黑名单中
                        var blacklistService = context.HttpContext.RequestServices
                            .GetRequiredService<TokenBlacklistService>();

                        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        var issuedAtClaim = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Iat);

                        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(issuedAtClaim))
                        {
                            if (long.TryParse(issuedAtClaim, out var issuedAt))
                            {
                                if (blacklistService.IsTokenBlacklisted(userId, issuedAt))
                                {
                                    context.Fail("Token has been revoked");
                                    return Task.CompletedTask;
                                }
                            }
                        }

                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// 添加授权策略
    /// </summary>
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // AdminOnly: 只有 Admin 可访问
            options.AddPolicy(AuthPolicies.AdminOnly, policy =>
                policy.RequireRole(UserRoles.Admin));

            // OperatorOrAbove: Admin 或 Operator 可访问
            options.AddPolicy(AuthPolicies.OperatorOrAbove, policy =>
                policy.RequireRole(UserRoles.Admin, UserRoles.Operator));

            // AllAuthenticated: 所有已认证用户（默认策略）
            options.AddPolicy(AuthPolicies.AllAuthenticated, policy =>
                policy.RequireAuthenticatedUser());

            // 设置默认策略为 AllAuthenticated
            options.DefaultPolicy = options.GetPolicy(AuthPolicies.AllAuthenticated)!;
        });

        return services;
    }

    /// <summary>
    /// 添加后台服务
    /// </summary>
    public static IServiceCollection AddBackgroundServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 实时广播
        services.AddHostedService<TelemetryBroadcastService>();

        // 采集规则引擎
        services.AddHostedService<CollectionRuleEngine>();

        // 数据清理
        services.Configure<DataCleanupOptions>(
            configuration.GetSection(DataCleanupOptions.SectionName));
        services.AddHostedService<DataCleanupService>();

        // 数据聚合 - 仅 SQLite 需要，TimescaleDB 使用连续聚合
        var dbProvider = configuration["DatabaseProvider"] ?? "Sqlite";
        if (dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<DataAggregationService>();
        }

        // v56.1: 统计缓存服务 - 避免 COUNT(*) 全表扫描
        services.AddSingleton<StatsCacheService>();
        services.AddHostedService(sp => sp.GetRequiredService<StatsCacheService>());

        // v60: 健康评估后台服务 - 每60秒评估所有设备
        services.AddHostedService<HealthAssessmentBackgroundService>();

        // v62: 动态基线后台服务 - 定期更新设备基线
        services.AddHostedService<DynamicBaselineBackgroundService>();

        // v64: 电机基线学习后台服务 - 定期执行增量学习
        services.Configure<MotorBaselineLearningOptions>(
            configuration.GetSection("MotorBaselineLearning"));
        services.AddHostedService<MotorBaselineLearningBackgroundService>();

        // v64: 电机诊断后台服务 - 定期执行故障诊断
        services.Configure<MotorDiagnosisOptions>(
            configuration.GetSection("MotorDiagnosis"));
        services.AddHostedService<MotorDiagnosisBackgroundService>();

        return services;
    }

    /// <summary>
    /// 添加 CORS 策略
    /// </summary>
    public static IServiceCollection AddCorsPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            // 开发环境策略
            options.AddPolicy("development", policy =>
            {
                policy.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://localhost:3002", "http://127.0.0.1:3000")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });

            // 生产环境策略
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "https://localhost" };
            options.AddPolicy("production", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }
}
