using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Serilog;

namespace IntelliMaint.Host.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time telemetry data broadcasting
/// Requires authentication - all connected users must have valid JWT token
/// </summary>
[Authorize]
public sealed class TelemetryHub : Hub
{
    private const string AllGroupName = "all";

    public override Task OnConnectedAsync()
    {
        Log.Information("SignalR connected: ConnectionId={ConnectionId}, User={User}, Remote={Remote}",
            Context.ConnectionId,
            Context.User?.Identity?.Name ?? "anonymous",
            Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            Log.Information("SignalR disconnected: ConnectionId={ConnectionId}", Context.ConnectionId);
        }
        else
        {
            Log.Warning(exception, "SignalR disconnected with error: ConnectionId={ConnectionId}", Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to all telemetry data
    /// </summary>
    public Task SubscribeAll()
    {
        Log.Debug("SignalR SubscribeAll: ConnectionId={ConnectionId}", Context.ConnectionId);
        return Groups.AddToGroupAsync(Context.ConnectionId, AllGroupName);
    }

    /// <summary>
    /// Subscribe to a specific device's telemetry data
    /// </summary>
    public Task SubscribeDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new HubException("deviceId is required");

        var group = $"device:{deviceId}";
        Log.Debug("SignalR SubscribeDevice: ConnectionId={ConnectionId}, Group={Group}", Context.ConnectionId, group);

        return Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    /// <summary>
    /// Unsubscribe from a specific device
    /// </summary>
    public Task UnsubscribeDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new HubException("deviceId is required");

        var group = $"device:{deviceId}";
        Log.Debug("SignalR UnsubscribeDevice: ConnectionId={ConnectionId}, Group={Group}", Context.ConnectionId, group);

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    /// <summary>
    /// Unsubscribe from all telemetry data
    /// </summary>
    public Task UnsubscribeAll()
    {
        Log.Debug("SignalR UnsubscribeAll: ConnectionId={ConnectionId}", Context.ConnectionId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, AllGroupName);
    }
}
