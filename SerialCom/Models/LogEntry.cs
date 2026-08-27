using System;
using System.Windows.Media;

namespace SerialCom.Models;

/// <summary>
/// 一条通信日志记录。
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>方向：TX=发送，RX=接收，SYS=系统消息</summary>
    public string Direction { get; init; } = "SYS";

    /// <summary>显示用文本（已按格式化后的字符串）</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>方向对应的前景色，便于 XAML 直接绑定着色。</summary>
    public Brush DirectionBrush
    {
        get
        {
            return Direction switch
            {
                "TX" => Brushes.DodgerBlue,
                "RX" => Brushes.SeaGreen,
                "ERR" => Brushes.OrangeRed,
                _ => Brushes.DimGray
            };
        }
    }

    public override string ToString() => $"[{Timestamp:HH:mm:ss.fff}] [{Direction}] {Text}";
}
