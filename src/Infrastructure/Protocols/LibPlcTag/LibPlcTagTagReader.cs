using System.Diagnostics;
using IntelliMaint.Core.Contracts;
using libplctag;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// Batch read tags using libplctag.
/// Handles TIMEOUT/NO_ROUTE/BAD_TAG/TYPE_MISMATCH classification.
/// Quality is set to 192 (OPC Good) on success.
/// </summary>
public sealed class LibPlcTagTagReader
{
    private readonly ILogger<LibPlcTagTagReader> _logger;

    public LibPlcTagTagReader(ILogger<LibPlcTagTagReader> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagReadResult>> ReadBatchAsync(
        PooledTagGroup tagGroup,
        PlcEndpointConfig plcConfig,
        CancellationToken ct)
    {
        var results = new List<TagReadResult>(tagGroup.Tags.Count);
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < tagGroup.Tags.Count && i < tagGroup.Config.Tags.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var tag = tagGroup.Tags[i];
            var tagConfig = tagGroup.Config.Tags[i];

            try
            {
                // Read from PLC
                await tag.ReadAsync(ct);

                // Get value based on CIP type
                var rawValue = ReadValue(tag, tagConfig.CipType);
                var latencyMs = stopwatch.Elapsed.TotalMilliseconds;

                results.Add(new TagReadResult(
                    PlcId: plcConfig.PlcId,
                    DeviceId: plcConfig.PlcId,
                    TagId: tagConfig.TagId,
                    TagConfig: tagConfig,
                    Success: true,
                    RawValue: rawValue,
                    Quality: 192,  // Good
                    Error: LibPlcTagError.OK,
                    ErrorMessage: null,
                    LatencyMs: latencyMs));
            }
            catch (Exception ex)
            {
                var error = MapExceptionToError(ex);
                results.Add(new TagReadResult(
                    PlcId: plcConfig.PlcId,
                    DeviceId: plcConfig.PlcId,
                    TagId: tagConfig.TagId,
                    TagConfig: tagConfig,
                    Success: false,
                    RawValue: null,
                    Quality: 0,
                    Error: error,
                    ErrorMessage: ex.Message,
                    LatencyMs: stopwatch.Elapsed.TotalMilliseconds));

                _logger.LogWarning("Tag read failed: {TagId} - {Error}: {Message}", 
                    tagConfig.TagId, error, ex.Message);
            }

            stopwatch.Restart();
        }

        return results;
    }

    private static object ReadValue(Tag tag, string cipType)
    {
        var type = cipType.Trim().ToUpperInvariant();
        
        return type switch
        {
            "BOOL" => tag.GetBit(0),
            "SINT" => tag.GetInt8(0),
            "USINT" => tag.GetUInt8(0),
            "INT" => tag.GetInt16(0),
            "UINT" => tag.GetUInt16(0),
            "DINT" => tag.GetInt32(0),
            "UDINT" => tag.GetUInt32(0),
            "LINT" => tag.GetInt64(0),
            "ULINT" => tag.GetUInt64(0),
            "REAL" => tag.GetFloat32(0),
            "LREAL" => tag.GetFloat64(0),
            "STRING" => ReadAbString(tag),
            _ => throw new InvalidOperationException($"Unsupported CIP type: {cipType}")
        };
    }

    /// <summary>
    /// Read AB STRING: [4 bytes LEN][82 bytes DATA]
    /// </summary>
    private static byte[] ReadAbString(Tag tag)
    {
        // AB STRING default size is 88 bytes (4 len + 82 data + 2 padding)
        // Read as raw bytes for TypeMapper to parse
        var size = tag.GetSize();
        var bytes = new byte[size];
        
        for (int i = 0; i < size; i++)
        {
            bytes[i] = tag.GetUInt8(i);
        }
        
        return bytes;
    }

    /// <summary>
    /// Map exception to error code by analyzing message
    /// </summary>
    private static LibPlcTagError MapExceptionToError(Exception ex)
    {
        var msg = (ex.Message ?? string.Empty).ToUpperInvariant();
        
        if (msg.Contains("TIMEOUT") || msg.Contains("TIMED OUT"))
            return LibPlcTagError.TIMEOUT;
            
        if (msg.Contains("NOT FOUND") || msg.Contains("BAD TAG") || msg.Contains("UNKNOWN TAG"))
            return LibPlcTagError.BAD_TAG;
            
        if (msg.Contains("NO ROUTE") || msg.Contains("CONNECTION") || msg.Contains("UNABLE TO CONNECT"))
            return LibPlcTagError.NO_ROUTE;
            
        if (msg.Contains("TOO MANY") || msg.Contains("BUSY"))
            return LibPlcTagError.TOO_MANY_CONN;
            
        if (msg.Contains("TYPE") || msg.Contains("MISMATCH") || msg.Contains("DATA"))
            return LibPlcTagError.TYPE_MISMATCH;
            
        return LibPlcTagError.UNKNOWN;
    }
}

/// <summary>
/// Tag read result
/// </summary>
public sealed record TagReadResult(
    string PlcId,
    string DeviceId,
    string TagId,
    PlcTagConfig TagConfig,
    bool Success,
    object? RawValue,
    int Quality,
    LibPlcTagError Error,
    string? ErrorMessage,
    double LatencyMs);
