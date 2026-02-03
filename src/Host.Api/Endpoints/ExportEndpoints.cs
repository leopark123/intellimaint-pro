using System.Text;
using ClosedXML.Excel;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/export")
            .WithTags("Export")
            .RequireAuthorization(AuthPolicies.AllAuthenticated);  // 所有已认证用户可导出

        group.MapGet("/telemetry/csv", ExportTelemetryCsvAsync)
            .WithName("ExportTelemetryCsv")
            .WithSummary("导出遥测数据为 CSV");

        group.MapGet("/telemetry/xlsx", ExportTelemetryXlsxAsync)
            .WithName("ExportTelemetryXlsx")
            .WithSummary("导出遥测数据为 Excel");

        group.MapGet("/alarms/csv", ExportAlarmsCsvAsync)
            .WithName("ExportAlarmsCsv")
            .WithSummary("导出告警数据为 CSV");

        group.MapGet("/alarms/xlsx", ExportAlarmsXlsxAsync)
            .WithName("ExportAlarmsXlsx")
            .WithSummary("导出告警数据为 Excel");
    }

    private static async Task<IResult> ExportTelemetryCsvAsync(
        [FromServices] ITelemetryRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] string? tagId,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
            var maxLimit = Math.Min(limit ?? SystemConstants.Export.DefaultLimit, SystemConstants.Export.MaxLimit);
            var points = await repo.QuerySimpleAsync(deviceId, tagId, startTs, endTs, maxLimit, ct);

            var fileName = $"telemetry_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            // 流式输出：避免全量加载到内存
            return Results.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 8192, leaveOpen: true);

                // BOM for Excel UTF-8 compatibility
                await writer.WriteAsync("\uFEFF");
                // Header
                await writer.WriteLineAsync("DeviceId,TagId,Timestamp,Value,ValueType,Quality,Unit");

                foreach (var p in points)
                {
                    var ts = DateTimeOffset.FromUnixTimeMilliseconds(p.Ts).ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var value = ExtractValue(p)?.ToString() ?? "";
                    await writer.WriteLineAsync($"{Escape(p.DeviceId)},{Escape(p.TagId)},{ts},{Escape(value)},{p.ValueType},{p.Quality},{Escape(p.Unit ?? "")}");
                }

                await writer.FlushAsync(ct);
            }, contentType: "text/csv; charset=utf-8", fileDownloadName: fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export telemetry CSV");
            return Results.Problem("导出失败");
        }
    }

    private static async Task<IResult> ExportTelemetryXlsxAsync(
        [FromServices] ITelemetryRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] string? tagId,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        try
        {
            var maxLimit = Math.Min(limit ?? SystemConstants.Export.DefaultLimit, SystemConstants.Export.MaxLimit);
            var points = await repo.QuerySimpleAsync(deviceId, tagId, startTs, endTs, maxLimit, ct);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Telemetry");

            // Header
            worksheet.Cell(1, 1).Value = "DeviceId";
            worksheet.Cell(1, 2).Value = "TagId";
            worksheet.Cell(1, 3).Value = "Timestamp";
            worksheet.Cell(1, 4).Value = "Value";
            worksheet.Cell(1, 5).Value = "ValueType";
            worksheet.Cell(1, 6).Value = "Quality";
            worksheet.Cell(1, 7).Value = "Unit";

            // Style header
            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Data
            var row = 2;
            foreach (var p in points)
            {
                worksheet.Cell(row, 1).Value = p.DeviceId;
                worksheet.Cell(row, 2).Value = p.TagId;
                worksheet.Cell(row, 3).Value = DateTimeOffset.FromUnixTimeMilliseconds(p.Ts).DateTime;
                worksheet.Cell(row, 4).SetValue(ExtractValue(p)?.ToString() ?? "");
                worksheet.Cell(row, 5).Value = p.ValueType.ToString();
                worksheet.Cell(row, 6).Value = p.Quality;
                worksheet.Cell(row, 7).Value = p.Unit ?? "";
                row++;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var bytes = stream.ToArray();

            var fileName = $"telemetry_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export telemetry Excel");
            return Results.Problem("导出失败");
        }
    }

    private static async Task<IResult> ExportAlarmsCsvAsync(
        [FromServices] IAlarmRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] int? status,
        [FromQuery] int? minSeverity,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        try
        {
            var maxLimit = Math.Min(limit ?? SystemConstants.Export.DefaultLimit, SystemConstants.Export.MaxLimit);
            var query = new AlarmQuery
            {
                DeviceId = deviceId,
                Status = status.HasValue ? (AlarmStatus)status.Value : null,
                MinSeverity = minSeverity,
                StartTs = startTs,
                EndTs = endTs,
                Limit = maxLimit
            };

            var result = await repo.QueryAsync(query, ct);

            var fileName = $"alarms_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            // 流式输出：避免全量加载到内存
            return Results.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 8192, leaveOpen: true);

                await writer.WriteAsync("\uFEFF");
                await writer.WriteLineAsync("AlarmId,DeviceId,TagId,Timestamp,Severity,Code,Message,Status,AckedBy,AckedTime,AckNote");

                foreach (var a in result.Items)
                {
                    var ts = DateTimeOffset.FromUnixTimeMilliseconds(a.Ts).ToString("yyyy-MM-dd HH:mm:ss");
                    var ackedTime = a.AckedUtc.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(a.AckedUtc.Value).ToString("yyyy-MM-dd HH:mm:ss")
                        : "";

                    await writer.WriteLineAsync($"{Escape(a.AlarmId)},{Escape(a.DeviceId)},{Escape(a.TagId ?? "")},{ts},{a.Severity},{Escape(a.Code)},{Escape(a.Message)},{a.Status},{Escape(a.AckedBy ?? "")},{ackedTime},{Escape(a.AckNote ?? "")}");
                }

                await writer.FlushAsync(ct);
            }, contentType: "text/csv; charset=utf-8", fileDownloadName: fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export alarms CSV");
            return Results.Problem("导出失败");
        }
    }

    private static async Task<IResult> ExportAlarmsXlsxAsync(
        [FromServices] IAlarmRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] int? status,
        [FromQuery] int? minSeverity,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        try
        {
            var maxLimit = Math.Min(limit ?? SystemConstants.Export.DefaultLimit, SystemConstants.Export.MaxLimit);
            var query = new AlarmQuery
            {
                DeviceId = deviceId,
                Status = status.HasValue ? (AlarmStatus)status.Value : null,
                MinSeverity = minSeverity,
                StartTs = startTs,
                EndTs = endTs,
                Limit = maxLimit
            };

            var result = await repo.QueryAsync(query, ct);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Alarms");

            // Header
            worksheet.Cell(1, 1).Value = "AlarmId";
            worksheet.Cell(1, 2).Value = "DeviceId";
            worksheet.Cell(1, 3).Value = "TagId";
            worksheet.Cell(1, 4).Value = "Timestamp";
            worksheet.Cell(1, 5).Value = "Severity";
            worksheet.Cell(1, 6).Value = "Code";
            worksheet.Cell(1, 7).Value = "Message";
            worksheet.Cell(1, 8).Value = "Status";
            worksheet.Cell(1, 9).Value = "AckedBy";
            worksheet.Cell(1, 10).Value = "AckedTime";
            worksheet.Cell(1, 11).Value = "AckNote";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

            var row = 2;
            foreach (var a in result.Items)
            {
                worksheet.Cell(row, 1).Value = a.AlarmId;
                worksheet.Cell(row, 2).Value = a.DeviceId;
                worksheet.Cell(row, 3).Value = a.TagId ?? "";
                worksheet.Cell(row, 4).Value = DateTimeOffset.FromUnixTimeMilliseconds(a.Ts).DateTime;
                worksheet.Cell(row, 5).Value = a.Severity.ToString();
                worksheet.Cell(row, 6).Value = a.Code;
                worksheet.Cell(row, 7).Value = a.Message;
                worksheet.Cell(row, 8).Value = a.Status.ToString();
                worksheet.Cell(row, 9).Value = a.AckedBy ?? "";
                worksheet.Cell(row, 10).Value = a.AckedUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(a.AckedUtc.Value).DateTime
                    : "";
                worksheet.Cell(row, 11).Value = a.AckNote ?? "";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var bytes = stream.ToArray();

            var fileName = $"alarms_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export alarms Excel");
            return Results.Problem("导出失败");
        }
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static object? ExtractValue(TelemetryPoint p)
    {
        return p.ValueType switch
        {
            TagValueType.Bool => p.BoolValue,
            TagValueType.Int8 => p.Int8Value,
            TagValueType.UInt8 => p.UInt8Value,
            TagValueType.Int16 => p.Int16Value,
            TagValueType.UInt16 => p.UInt16Value,
            TagValueType.Int32 => p.Int32Value,
            TagValueType.UInt32 => p.UInt32Value,
            TagValueType.Int64 => p.Int64Value,
            TagValueType.UInt64 => p.UInt64Value,
            TagValueType.Float32 => p.Float32Value,
            TagValueType.Float64 => p.Float64Value,
            TagValueType.String => p.StringValue,
            TagValueType.ByteArray => p.ByteArrayValue != null ? Convert.ToBase64String(p.ByteArrayValue) : null,
            TagValueType.DateTime => p.Int64Value,
            _ => null
        };
    }
}
