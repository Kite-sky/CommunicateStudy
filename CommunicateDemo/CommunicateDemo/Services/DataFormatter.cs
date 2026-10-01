using System;
using System.Collections.Generic;
using System.Text;

namespace CommunicateDemo.Services;
/// <summary>
/// 消息数据格式枚举
/// </summary>
public enum MessageFormat
{
    Ascii, // 文本字符串
    Hex   // 16进制
}

/// <summary>
/// 编码类型枚举
/// </summary>
public enum TextEncoding
{
    Utf8,
    Gbk,
    Ascii
}

/// <summary>
/// 数据格式化工具：负责 byte[] ↔ string 的双向转换
/// </summary>
public static class MessageFormatter
{
    /// <summary>根据编码名称获取 Encoding 对象</summary>
    public static Encoding GetEncoding(TextEncoding enc) => enc switch
    {
        TextEncoding.Utf8 => Encoding.UTF8,
        TextEncoding.Gbk => Encoding.GetEncoding("GBK"),
        TextEncoding.Ascii => Encoding.ASCII,
        _ => Encoding.UTF8
    };

    /// <summary>
    /// 将用户输入字符串（可能是 Hex 或文本）转为 byte[]
    /// </summary>
    public static byte[] StringToBytes(string input, MessageFormat format, TextEncoding enc)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();

        if (format == MessageFormat.Hex)
        {
            return HexStringToBytes(input);
        }
        return GetEncoding(enc).GetBytes(input);
    }

    /// <summary>
    /// 将 byte[] 转为显示字符串（Hex 显示或按编码解码）
    /// </summary>
    public static string BytesToString(byte[] data, MessageFormat format, TextEncoding enc)
    {
        if (data == null || data.Length == 0) return string.Empty;

        if (format == MessageFormat.Hex)
        {
            return BytesToHexString(data);
        }
        return GetEncoding(enc).GetString(data);
    }

    // ========== Hex 工具方法 ==========

    /// <summary>字节数组 → 空格分隔的 Hex 字符串，如 "48 65 6C 6C 6F"</summary>
    public static string BytesToHexString(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        return BitConverter.ToString(data).Replace('-', ' ');
    }

    /// <summary>Hex 字符串 → 字节数组，支持 "0x" 前缀、无空格/单空格/多空格/逗号分隔</summary>
    public static byte[] HexStringToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();

        // 清理：去掉 0x 前缀、所有空白、逗号
        var clean = hex
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace("0X", "", StringComparison.Ordinal)
            .Replace(",", "")
            .Replace(" ", "")
            .Replace("\t", "")
            .Replace("\r", "")
            .Replace("\n", "");

        if (clean.Length % 2 != 0)
            clean = "0" + clean;

        var result = new byte[clean.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        }
        return result;
    }

    /// <summary>验证是否为合法的 Hex 字符串</summary>
    public static bool IsValidHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var clean = hex
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "")
            .Replace(" ", "")
            .Replace("\t", "")
            .Replace("\r", "")
            .Replace("\n", "");
        return clean.Length > 0 && clean.Length % 2 == 0
            && clean.All(c => Uri.IsHexDigit(c));
    }
}

