namespace SerialCom.Models;

/// <summary>
/// 通信模式：RS232 为全双工点对点，RS485 通常为半双工总线。
/// </summary>
public enum CommunicationMode
{
    /// <summary>RS-232：标准全双工串口</summary>
    Rs232 = 0,

    /// <summary>RS-485：半双工总线，通过 RTS 控制收发方向</summary>
    Rs485 = 1
}
