using SerialCom.Models;

namespace SerialCom.Services;

/// <summary>
/// 串口服务工厂：根据通信模式创建对应实现。
/// 由于每次切换模式需重新创建实例，工厂方法返回新实例。
/// </summary>
public interface ISerialServiceFactory
{
    /// <summary>按指定通信模式创建一个串口服务实例。</summary>
    ISerialService Create(CommunicationMode mode);
}

/// <summary>
/// 默认工厂实现，直接 new 出 RS-232 / RS-485 服务实例。
/// </summary>
public class SerialServiceFactory : ISerialServiceFactory
{
    public ISerialService Create(CommunicationMode mode)
    {
        return mode switch
        {
            CommunicationMode.Rs232 => new Rs232Service(),
            CommunicationMode.Rs485 => new Rs485Service(),
            _ => new Rs232Service()
        };
    }
}
