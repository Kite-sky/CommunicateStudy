using System;
using System.Threading;
using System.Threading.Tasks;
using SerialCom.Models;

namespace SerialCom.Services;

/// <summary>
/// 串口通信服务统一抽象，支持 RS-232 与 RS-485。
/// </summary>
public interface ISerialService : IDisposable
{
    /// <summary>当前通信模式</summary>
    CommunicationMode Mode { get; }

    /// <summary>是否已打开</summary>
    bool IsOpen { get; }

    /// <summary>参数变更时触发</summary>
    event EventHandler? StateChanged;

    /// <summary>收到一帧数据时触发（在串口工作线程上触发，订阅者需自行切换到 UI 线程）</summary>
    event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <summary>发生错误时触发</summary>
    event EventHandler<ErrorEventArgs>? ErrorOccurred;

    /// <summary>使用给定配置打开串口</summary>
    void Open(SerialConfig config);

    /// <summary>关闭串口</summary>
    void Close();

    /// <summary>
    /// 异步发送字节。实现必须保证调用线程不被阻塞（内部卸载到后台线程），
    /// 可安全在 UI 线程调用；RS-485 的方向切换等待由实现方在后台完成。
    /// 并发调用由实现方串行化，返回实际写入的字节数。
    /// </summary>
    Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>获取可用串口名列表</summary>
    string[] GetAvailablePorts();
}

/// <summary>
/// 接收数据事件参数。
/// </summary>
public class DataReceivedEventArgs : EventArgs
{
    public byte[] Data { get; }

    public DataReceivedEventArgs(byte[] data)
    {
        Data = data;
    }
}

/// <summary>
/// 错误事件参数。
/// </summary>
public class ErrorEventArgs : EventArgs
{
    public string Message { get; }

    public ErrorEventArgs(string message)
    {
        Message = message;
    }
}
