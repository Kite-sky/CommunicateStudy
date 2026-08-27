using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SerialCom.Models;

namespace SerialCom.Services;

/// <summary>
/// RS-485 半双工服务。通过手动控制 RTS 切换总线方向：
///   发送前置 RTS 为发送使能电平，发送完后再置回接收状态。
/// 注意：不同 RS-485 收发器（如 MAX485）的方向控制电平可能不同，
///       可通过 <see cref="SerialConfig.RtsHighWhenTransmitting"/> 指定发送时 RTS 是否为高电平。
/// </summary>
public class Rs485Service : SerialServiceBase
{
    public override CommunicationMode Mode => CommunicationMode.Rs485;

    protected override void ApplyPortSettings(SerialPort port, SerialConfig config)
    {
        // RS-485 通常需要手动控制 RTS，所以强制关闭硬件握手
        port.Handshake = Handshake.None;
        port.RtsEnable = false; // 默认处于接收状态
    }

    /// <summary>
    /// RS-485 写入：先切换 RTS 到发送方向，写完并等待硬件 FIFO 清空后再切回接收方向。
    /// 本方法由基类 SendAsync 卸载到线程池执行；内部等待全部用 Task.Delay 异步完成，
    /// 不占用线程，也不会阻塞 UI。
    /// </summary>
    protected override async Task<int> WriteBytesCoreAsync(SerialPort port, byte[] data, CancellationToken cancellationToken)
    {
        var config = CurrentConfig;
        bool txHigh = config?.RtsHighWhenTransmitting ?? true;

        // 1) 切换到发送方向
        cancellationToken.ThrowIfCancellationRequested();
        port.RtsEnable = txHigh;
        // 给收发器方向切换留出稳定时间（典型 50~200us，这里保守 1ms）
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        // 2) 写入数据
        //    注意：SerialPort.BaseStream.Flush() 在 .NET Core / .NET 5+ 中是空实现（no-op），
        //    因此我们依赖 Write 返回后再轮询 BytesToWrite==0 来等待硬件 FIFO 清空。
        port.Write(data, 0, data.Length);

        // 3) 等待最后字节真正移出硬件发送 FIFO，再切回接收方向
        await WaitForWriteBufferEmptyAsync(port, data.Length, cancellationToken).ConfigureAwait(false);
        port.RtsEnable = !txHigh;

        return data.Length;
    }

    /// <summary>
    /// 异步等待串口发送缓冲区数据物理发送完成。
    /// 兼容 USB 转串口芯片 BytesToWrite 无效问题：轮询失败时按波特率估算兜底。
    /// </summary>
    /// <param name="port">串口实例</param>
    /// <param name="byteCount">本次发送字节数</param>
    /// <param name="cancellationToken">取消令牌（用于串口关闭/退出时快速结束）</param>
    private static async Task WaitForWriteBufferEmptyAsync(SerialPort port, int byteCount, CancellationToken cancellationToken)
    {
        int baud = port.BaudRate > 0 ? port.BaudRate : 9600;
        // 1起始位 + 8数据位 + 1停止位，每字节10bit
        int perByteUs = (int)(10.0 / baud * 1_000_000);
        int totalEstimateUs = (perByteUs * byteCount) + 500; // +500us 保守缓冲

        // 方案A：轮询 BytesToWrite（部分 USB 转串口芯片驱动可能始终为 0，需双保险）
        int maxPollLoops = Math.Max(20, byteCount * 2);
        int polled = 0;
        while (polled < maxPollLoops && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (port.BytesToWrite == 0)
                {
                    // 缓冲区空后，额外等待一半预估时间，保证最后 bit 送出
                    int tailSleepMs = Math.Max(1, ((totalEstimateUs / 2) + 999) / 1000);
                    await Task.Delay(tailSleepMs, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch
            {
                // 读取 BytesToWrite 异常（串口断开等），跳出轮询进入兜底延时
                break;
            }
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            polled++;
        }

        // 方案B：波特率估算兜底延时
        int delayMs = (totalEstimateUs + 999) / 1000;
        if (delayMs < 1) delayMs = 1;
        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
    }
}
