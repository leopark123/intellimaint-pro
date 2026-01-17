using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Serilog;

namespace IntelliMaint.Host.Api.Middleware;

/// <summary>
/// v65: 全局异常处理中间件
/// 统一处理未捕获异常，返回一致的错误响应格式
/// 支持 TraceId 用于生产环境问题追踪
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 请求被取消，正常关闭，不记录错误
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            Log.Error(ex, "Unhandled exception for {Method} {Path} [TraceId: {TraceId}]",
                context.Request.Method, context.Request.Path, traceId);

            await HandleExceptionAsync(context, ex, traceId);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string traceId)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, errorCode, message) = exception switch
        {
            ArgumentNullException => (HttpStatusCode.BadRequest, "ARGUMENT_NULL", "必填参数不能为空"),
            ArgumentException argEx => (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", argEx.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND", "资源不存在"),
            UnauthorizedAccessException => (HttpStatusCode.Forbidden, "FORBIDDEN", "无权访问"),
            InvalidOperationException invEx => (HttpStatusCode.BadRequest, "INVALID_OPERATION", invEx.Message),
            TimeoutException => (HttpStatusCode.GatewayTimeout, "TIMEOUT", "请求超时"),
            NotImplementedException => (HttpStatusCode.NotImplemented, "NOT_IMPLEMENTED", "功能暂未实现"),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "服务器内部错误")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new ErrorResponse
        {
            Success = false,
            Error = message,
            ErrorCode = errorCode,
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            // 仅在开发环境显示详细错误信息
            Details = _env.IsDevelopment() ? exception.ToString() : null
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

/// <summary>
/// 统一错误响应格式
/// </summary>
public sealed record ErrorResponse
{
    public bool Success { get; init; } = false;
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public string? TraceId { get; init; }
    public long Timestamp { get; init; }
    public string? Details { get; init; }
}

/// <summary>
/// 中间件扩展方法
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
