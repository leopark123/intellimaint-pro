using System.Buffers.Binary;
using System.Text;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// CIP type -> ValueType mapping + strict value materialization.
/// Fail Fast: no implicit conversions.
/// </summary>
public sealed class LibPlcTagTypeMapper : ITagTypeMapper
{
    public TagValueType MapType(string protocol, string tagId, string? tagTypeHint, object? rawValue)
    {
        if (!string.Equals(protocol, "libplctag", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported protocol for LibPlcTagTypeMapper.", nameof(protocol));

        var hint = (tagTypeHint ?? string.Empty).Trim().ToUpperInvariant();
        if (hint.Length == 0)
            throw new ArgumentException($"TagTypeHint is required for libplctag tags. tagId={tagId}");

        return hint switch
        {
            "BOOL" => TagValueType.Bool,
            "SINT" => TagValueType.Int8,
            "USINT" => TagValueType.UInt8,
            "INT" => TagValueType.Int16,
            "UINT" => TagValueType.UInt16,
            "DINT" => TagValueType.Int32,
            "UDINT" => TagValueType.UInt32,
            "LINT" => TagValueType.Int64,
            "ULINT" => TagValueType.UInt64,
            "REAL" => TagValueType.Float32,
            "LREAL" => TagValueType.Float64,
            "STRING" => TagValueType.String,
            _ => throw new ArgumentOutOfRangeException(nameof(tagTypeHint), $"Unsupported CIP type '{tagTypeHint}' for tagId={tagId}.")
        };
    }

    public TelemetryPoint MapValue(
        string deviceId,
        string tagId,
        TagValueType expectedType,
        object rawValue,
        int quality = 192,
        string? protocol = null)
    {
        protocol ??= "libplctag";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seq = TelemetryPoint.GenerateSeq();

        try
        {
            return expectedType switch
            {
                TagValueType.Bool => MapBool(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.Int8 => MapInt8(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.UInt8 => MapUInt8(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.Int16 => MapInt16(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.UInt16 => MapUInt16(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.Int32 => MapInt32(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.UInt32 => MapUInt32(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.Int64 => MapInt64(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.UInt64 => MapUInt64(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.Float32 => MapFloat32(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.Float64 => MapFloat64(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                TagValueType.String => MapAbString(deviceId, tagId, ts, seq, rawValue, quality, protocol),
                _ => throw new InvalidOperationException($"ValueType '{expectedType}' not supported by LibPlcTagTypeMapper.")
            };
        }
        catch (Exception ex) when (ex is InvalidCastException or ArgumentException or ArgumentOutOfRangeException)
        {
            throw new LibPlcTagTypeMismatchException(deviceId, tagId, expectedType, rawValue?.GetType().FullName, ex);
        }
    }

    private static TelemetryPoint MapBool(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        bool value;
        if (raw is bool b)
        {
            value = b;
        }
        else if (raw is byte ub)
        {
            if (ub is not (0 or 1)) throw new InvalidCastException("BOOL must be 0/1.");
            value = ub == 1;
        }
        else if (raw is int i)
        {
            if (i is not (0 or 1)) throw new InvalidCastException("BOOL must be 0/1.");
            value = i == 1;
        }
        else
        {
            throw new InvalidCastException($"Expected BOOL, got {raw?.GetType().Name}");
        }

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Bool,
            BoolValue = value,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapInt8(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not sbyte v)
            throw new InvalidCastException($"Expected SINT (sbyte), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Int8,
            Int8Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapUInt8(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not byte v)
            throw new InvalidCastException($"Expected USINT (byte), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.UInt8,
            UInt8Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapInt16(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not short v)
            throw new InvalidCastException($"Expected INT (short), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Int16,
            Int16Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapUInt16(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not ushort v)
            throw new InvalidCastException($"Expected UINT (ushort), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.UInt16,
            UInt16Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapInt32(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not int v)
            throw new InvalidCastException($"Expected DINT (int), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Int32,
            Int32Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapUInt32(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not uint v)
            throw new InvalidCastException($"Expected UDINT (uint), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.UInt32,
            UInt32Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapInt64(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not long v)
            throw new InvalidCastException($"Expected LINT (long), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Int64,
            Int64Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapUInt64(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not ulong v)
            throw new InvalidCastException($"Expected ULINT (ulong), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.UInt64,
            UInt64Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapFloat32(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not float v)
            throw new InvalidCastException($"Expected REAL (float), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Float32,
            Float32Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    private static TelemetryPoint MapFloat64(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        if (raw is not double v)
            throw new InvalidCastException($"Expected LREAL (double), got {raw?.GetType().Name}");

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.Float64,
            Float64Value = v,
            Quality = quality,
            Protocol = protocol
        };
    }

    /// <summary>
    /// AB STRING format:
    /// [4 bytes LEN (little-endian int32)][82 bytes DATA] => total 88 bytes (default)
    /// Must parse LEN; data is usually ASCII/UTF-8 compatible.
    /// </summary>
    private static TelemetryPoint MapAbString(string deviceId, string tagId, long ts, long seq, object raw, int quality, string protocol)
    {
        string value;

        if (raw is string s)
        {
            value = s;
        }
        else if (raw is byte[] bytes)
        {
            if (bytes.Length < 4)
                throw new InvalidCastException("AB STRING buffer too short (<4 bytes).");

            var len = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
            if (len < 0) len = 0;
            var maxData = Math.Min(len, Math.Max(0, bytes.Length - 4));
            value = Encoding.UTF8.GetString(bytes, 4, maxData);
        }
        else
        {
            throw new InvalidCastException($"Expected AB STRING as byte[] or string, got {raw?.GetType().Name}");
        }

        return new TelemetryPoint
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = ts,
            Seq = seq,
            ValueType = TagValueType.String,
            StringValue = value,
            Quality = quality,
            Protocol = protocol
        };
    }
}

/// <summary>
/// Type mismatch exception for libplctag
/// </summary>
public sealed class LibPlcTagTypeMismatchException : Exception
{
    public string DeviceId { get; }
    public string TagId { get; }
    public TagValueType Expected { get; }
    public string? ActualType { get; }

    public LibPlcTagTypeMismatchException(string deviceId, string tagId, TagValueType expected, string? actualType, Exception inner)
        : base($"TYPE_MISMATCH device={deviceId} tag={tagId} expected={expected} actual={actualType}", inner)
    {
        DeviceId = deviceId;
        TagId = tagId;
        Expected = expected;
        ActualType = actualType;
    }
}
