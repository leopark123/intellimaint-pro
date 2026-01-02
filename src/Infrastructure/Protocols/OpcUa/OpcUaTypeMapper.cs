using IntelliMaint.Core.Contracts;
using Opc.Ua;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

/// <summary>
/// OPC UA Variant/DataValue -> TagValueType mapping (strict; no implicit numeric conversions).
/// DateTime stored as Int64 epoch ms (TagValueType.DateTime).
/// ByteString stored as byte[] (TagValueType.ByteArray).
/// </summary>
public sealed class OpcUaTypeMapper
{
    public (TagValueType valueType, object materialized) MapVariant(
        string tagId,
        string? valueTypeHint,
        DataValue dv)
    {
        if (dv is null) throw new ArgumentNullException(nameof(dv));

        var v = dv.Value;
        if (v is null)
            throw new OpcUaTypeMismatchException(tagId, expectedHint: valueTypeHint, actual: "null");

        // If hint exists, enforce it strictly
        if (!string.IsNullOrWhiteSpace(valueTypeHint))
        {
            var expected = ParseHint(valueTypeHint!);
            var materialized = MaterializeStrict(tagId, expected, v);
            return (expected, materialized);
        }

        // No hint: infer from CLR type (strict)
        return v switch
        {
            bool b => (TagValueType.Bool, b),
            sbyte sb => (TagValueType.Int8, sb),
            byte ub => (TagValueType.UInt8, ub),
            short s => (TagValueType.Int16, s),
            ushort us => (TagValueType.UInt16, us),
            int i => (TagValueType.Int32, i),
            uint ui => (TagValueType.UInt32, ui),
            long l => (TagValueType.Int64, l),
            ulong ul => (TagValueType.UInt64, ul),
            float f => (TagValueType.Float32, f),
            double d => (TagValueType.Float64, d),
            string str => (TagValueType.String, str),
            DateTime dt => (TagValueType.DateTime, ToEpochMs(dt)),
            byte[] ba => (TagValueType.ByteArray, ba),
            _ => throw new OpcUaTypeMismatchException(tagId, expectedHint: null, actual: v.GetType().Name)
        };
    }

    public int MapQuality(DataValue dv)
    {
        if (dv.StatusCode == StatusCodes.Good) return 192;
        if (StatusCode.IsBad(dv.StatusCode)) return 0;
        return 64; // Uncertain
    }

    private static TagValueType ParseHint(string hint)
    {
        var h = hint.Trim();
        if (Enum.TryParse<TagValueType>(h, ignoreCase: true, out var vt))
            return vt;

        // Allow UA built-in names as hints
        return h.ToUpperInvariant() switch
        {
            "BOOLEAN" => TagValueType.Bool,
            "SBYTE" => TagValueType.Int8,
            "BYTE" => TagValueType.UInt8,
            "INT16" => TagValueType.Int16,
            "UINT16" => TagValueType.UInt16,
            "INT32" => TagValueType.Int32,
            "UINT32" => TagValueType.UInt32,
            "INT64" => TagValueType.Int64,
            "UINT64" => TagValueType.UInt64,
            "FLOAT" => TagValueType.Float32,
            "DOUBLE" => TagValueType.Float64,
            "STRING" => TagValueType.String,
            "DATETIME" => TagValueType.DateTime,
            "BYTESTRING" => TagValueType.ByteArray,
            _ => throw new ArgumentOutOfRangeException(nameof(hint), $"Unknown ValueTypeHint '{hint}'.")
        };
    }

    private static object MaterializeStrict(string tagId, TagValueType expected, object v) =>
        expected switch
        {
            TagValueType.Bool => Require<bool>(tagId, v),
            TagValueType.Int8 => Require<sbyte>(tagId, v),
            TagValueType.UInt8 => Require<byte>(tagId, v),
            TagValueType.Int16 => Require<short>(tagId, v),
            TagValueType.UInt16 => Require<ushort>(tagId, v),
            TagValueType.Int32 => Require<int>(tagId, v),
            TagValueType.UInt32 => Require<uint>(tagId, v),
            TagValueType.Int64 => Require<long>(tagId, v),
            TagValueType.UInt64 => Require<ulong>(tagId, v),
            TagValueType.Float32 => Require<float>(tagId, v),
            TagValueType.Float64 => Require<double>(tagId, v),
            TagValueType.String => Require<string>(tagId, v),
            TagValueType.DateTime => v is DateTime dt ? ToEpochMs(dt) : throw new OpcUaTypeMismatchException(tagId, expected.ToString(), v.GetType().Name),
            TagValueType.ByteArray => Require<byte[]>(tagId, v),
            _ => throw new ArgumentOutOfRangeException(nameof(expected), $"TagValueType '{expected}' not supported by OpcUaTypeMapper.")
        };

    private static T Require<T>(string tagId, object v)
    {
        if (v is T t) return t;
        throw new OpcUaTypeMismatchException(tagId, typeof(T).Name, v.GetType().Name);
    }

    private static long ToEpochMs(DateTime dt)
        => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}

public sealed class OpcUaTypeMismatchException : Exception
{
    public string TagId { get; }
    public string? Expected { get; }
    public string Actual { get; }

    public OpcUaTypeMismatchException(string tagId, string? expectedHint, string actual)
        : base($"TYPE_MISMATCH tag={tagId} expected={expectedHint ?? "(infer)"} actual={actual}")
    {
        TagId = tagId;
        Expected = expectedHint;
        Actual = actual;
    }
}
