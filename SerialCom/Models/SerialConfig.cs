using System.IO.Ports;
using System.Text;

namespace SerialCom.Models;

/// <summary>
/// 串口参数配置。
/// </summary>
public class SerialConfig
{
    /// <summary>串口名称，例如 COM3</summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>波特率</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>数据位</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>校验位</summary>
    public Parity Parity { get; set; } = Parity.None;

    /// <summary>停止位</summary>
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>握手方式</summary>
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>读超时（毫秒），-1 表示无限等待</summary>
    public int ReadTimeout { get; set; } = -1;

    /// <summary>写超时（毫秒），-1 表示无限等待</summary>
    public int WriteTimeout { get; set; } = -1;

    /// <summary>RS-485 模式下发送方向使能时的 RTS 状态（true=高电平驱动发送）</summary>
    public bool RtsHighWhenTransmitting { get; set; } = true;

    /// <summary>文本收发使用的编码；null 表示默认 ASCII。</summary>
    public Encoding? TextEncoding { get; set; }

    /// <summary>文本编码名称，用于序列化与持久化（等价于 Encoding.WebName）。</summary>
    public string TextEncodingName { get; set; } = "us-ascii";
}
