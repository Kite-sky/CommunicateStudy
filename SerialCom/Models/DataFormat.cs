namespace SerialCom.Models;

/// <summary>
/// 数据显示/发送格式（串口调试术语：ASCII 码 / HEX 十六进制）。
/// </summary>
public enum DataFormat
{
    /// <summary>ASCII 码（按字节显示为 ASCII 字符，控制字符转义）</summary>
    Ascii = 0,

    /// <summary>十六进制（每字节两位 hex，空格分隔）</summary>
    Hex = 1
}
