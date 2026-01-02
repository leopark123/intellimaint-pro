namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 统一数据类型枚举
/// 覆盖 OPC DA / OPC UA / libplctag / Modbus 所有类型
/// 注意：命名为 TagValueType 避免与 System.ValueType 冲突
/// </summary>
public enum TagValueType
{
    /// <summary>布尔值 (存储为 0/1)</summary>
    Bool = 1,
    
    /// <summary>有符号8位整数 (CIP: SINT)</summary>
    Int8 = 2,
    
    /// <summary>无符号8位整数 (CIP: USINT)</summary>
    UInt8 = 3,
    
    /// <summary>有符号16位整数 (CIP: INT, Modbus: INT16)</summary>
    Int16 = 4,
    
    /// <summary>无符号16位整数 (CIP: UINT, Modbus: UINT16)</summary>
    UInt16 = 5,
    
    /// <summary>有符号32位整数 (CIP: DINT, 最常见)</summary>
    Int32 = 6,
    
    /// <summary>无符号32位整数 (CIP: UDINT)</summary>
    UInt32 = 7,
    
    /// <summary>有符号64位整数 (CIP: LINT)</summary>
    Int64 = 8,
    
    /// <summary>无符号64位整数 (CIP: ULINT)</summary>
    UInt64 = 9,
    
    /// <summary>32位浮点数 (CIP: REAL, Modbus: FLOAT32)</summary>
    Float32 = 10,
    
    /// <summary>64位浮点数 (CIP: LREAL, Modbus: FLOAT64)</summary>
    Float64 = 11,
    
    /// <summary>字符串 (AB STRING 需要特殊处理)</summary>
    String = 12,
    
    /// <summary>PLC 时间戳</summary>
    DateTime = 13,
    
    /// <summary>原始字节数组 (BLOB)</summary>
    ByteArray = 14
}
