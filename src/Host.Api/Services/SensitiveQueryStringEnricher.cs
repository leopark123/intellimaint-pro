using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Serilog.Core;
using Serilog.Events;

namespace IntelliMaint.Host.Api.Services;

// Hosting.Diagnostics logs QueryString before middleware and again after the response.
// Redact the log property, leaving the actual request available to JWT/SignalR authentication.
public sealed class SensitiveQueryStringEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token", "accessToken", "refresh_token", "refreshToken",
        "token", "api_key", "apiKey", "password"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue("QueryString", out var property) ||
            property is not ScalarValue { Value: string query } || string.IsNullOrEmpty(query))
            return;

        var values = QueryHelpers.ParseQuery(query);
        if (!values.Keys.Any(SensitiveNames.Contains)) return;

        var redacted = QueryString.Create(values.SelectMany(pair => pair.Value.Select(value =>
            new KeyValuePair<string, string?>(pair.Key,
                SensitiveNames.Contains(pair.Key) ? "[REDACTED]" : value))));
        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("QueryString", redacted.Value));
    }
}
