using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SerialCom.Models;

namespace SerialCom.Services;

/// <summary>
/// 串口服务基类，封装 System.IO.Ports.SerialPort 的通用逻辑。
/// 子类通过重写 <see cref="ApplyPortSettings"/> 与 <see cref="WriteBytesCoreAsync"/> 实现 RS-232 / RS-485 差异。
/// </summary>
public abstract class SerialServiceBase : ISerialService
{
    private SerialPort? _port;
    private bool _disposed;
    private long _bytesSent;
    private long _bytesReceived;

    /// <summary>
    /// 发送串行锁：RS-485 的 RTS 方向翻转绝不能并发交错，
    /// 手动发送 / 周期发送同时触发时在此排队。
    /// </summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public abstract CommunicationMode Mode { get; }

    public bool IsOpen => _port?.IsOpen ?? false;

    /// <summary>累计发送字节数，线程安全。</summary>
    public long BytesSent => _bytesSent;

    /// <summary>累计接收字节数，线程安全。</summary>
    public long BytesReceived => _bytesReceived;

    /// <summary>当前打开时使用的配置，子类在 Open/Write 时可访问。</summary>
    protected SerialConfig? CurrentConfig { get; private set; }

    public event EventHandler? StateChanged;
    public event EventHandler<DataReceivedEventArgs>? DataReceived;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    public void Open(SerialConfig config)
    {
        if (IsOpen)
        {
            Close();
        }

        try
        {
            CurrentConfig = config;
            _port = new SerialPort
            {
                PortName = config.PortName,
                BaudRate = config.BaudRate,
                DataBits = config.DataBits,
                Parity = config.Parity,
                StopBits = config.StopBits,
                Handshake = config.Handshake,
                ReadTimeout = config.ReadTimeout,
                WriteTimeout = config.WriteTimeout,
                Encoding = config.TextEncoding ?? Encoding.ASCII
            };

            // 让子类应用特定模式设置（如 RS-485 的 RTS）
            ApplyPortSettings(_port, config);

            _port.DataReceived += OnSerialDataReceived;
            _port.ErrorReceived += OnSerialErrorReceived;

            _port.Open();
            _bytesSent = 0;
            _bytesReceived = 0;

            OnStateChanged();
        }
        catch (Exception ex)
        {
            RaiseError("打开串口失败：" + GetFriendlyMessage(ex));
            _port?.Dispose();
            _port = null;
            CurrentConfig = null;
            throw new InvalidOperationException(GetFriendlyMessage(ex), ex);
        }
    }

    public void Close()
    {
        if (_port == null)
        {
            return;
        }

        try
        {
            if (_port.IsOpen)
            {
                _port.DataReceived -= OnSerialDataReceived;
                _port.ErrorReceived -= OnSerialErrorReceived;
                _port.Close();
            }
        }
        catch (Exception ex)
        {
            RaiseError("关闭串口失败：" + GetFriendlyMessage(ex));
        }
        finally
        {
            _port.Dispose();
            _port = null;
            CurrentConfig = null;
            OnStateChanged();
        }
    }

    /// <summary>
    /// 异步发送：把 <see cref="WriteBytesCoreAsync"/>（含 RS-485 的等待逻辑）卸载到线程池执行，
    /// 绝不阻塞调用线程，可安全在 UI 线程 await；
    /// 并发调用通过 <see cref="_writeLock"/> 串行化，保证 RTS 方向翻转不交错。
    /// </summary>
    public async Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (!IsOpen || _port == null)
        {
            throw new InvalidOperationException("串口未打开");
        }
        if (data == null || data.Length == 0)
        {
            return 0;
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 发送核心（含 RS-485 的等待逻辑）必须离开调用线程执行，
            // 否则低波特率 + 大数据包时会把 UI 卡死数秒
            int sent = await Task.Run(() => WriteBytesCoreAsync(_port, data, cancellationToken), cancellationToken)
                                 .ConfigureAwait(false);
            Interlocked.Add(ref _bytesSent, sent);
            return sent;
        }
        catch (OperationCanceledException)
        {
            RaiseError("发送已取消");
            throw;
        }
        catch (Exception ex)
        {
            RaiseError("发送失败：" + GetFriendlyMessage(ex));
            throw new InvalidOperationException(GetFriendlyMessage(ex), ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public string[] GetAvailablePorts()
    {
        try
        {
            return SerialPort.GetPortNames();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>子类应用模式特定的端口设置。</summary>
    protected virtual void ApplyPortSettings(SerialPort port, SerialConfig config)
    {
        // 默认不做事
    }

    /// <summary>
    /// 子类实现具体写入逻辑。RS-232 直接写，RS-485 需要在写入前后切换 RTS 控制总线方向。
    /// 该方法始终在线程池上执行（由 <see cref="SendAsync"/> 卸载），
    /// 实现内的等待必须使用 Task.Delay 等异步方式，禁止 Thread.Sleep。
    /// 返回实际写入的字节数。
    /// </summary>
    protected abstract Task<int> WriteBytesCoreAsync(SerialPort port, byte[] data, CancellationToken cancellationToken);

    protected void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    protected void RaiseError(string message) => ErrorOccurred?.Invoke(this, new ErrorEventArgs(message));

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port == null || !_port.IsOpen)
        {
            return;
        }

        try
        {
            // 先休眠一小段时间，确保一帧数据尽量完整地到达缓冲区
            // 注意：DataReceived 触发不保证一次一帧，这里仅做尽力而为
            int bytesToRead = _port.BytesToRead;
            if (bytesToRead <= 0)
            {
                return;
            }

            var buffer = new byte[bytesToRead];
            int read = _port.Read(buffer, 0, bytesToRead);
            if (read > 0)
            {
                if (read < buffer.Length)
                {
                    Array.Resize(ref buffer, read);
                }
                System.Threading.Interlocked.Add(ref _bytesReceived, read);
                DataReceived?.Invoke(this, new DataReceivedEventArgs(buffer));
            }
        }
        catch (Exception ex)
        {
            // IOException 通常表示设备已拔出 / 串口失效
            if (ex is IOException)
            {
                RaiseError("串口异常：设备可能已被拔出或失连，已自动关闭。");
                try
                {
                    Close();
                }
                catch { /* 忽略二次异常 */ }
                return;
            }
            RaiseError("读取数据失败：" + GetFriendlyMessage(ex));
        }
    }

    private void OnSerialErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        var friendly = e.EventType switch
        {
            SerialError.RXOver => "接收缓冲区溢出，数据可能丢失（考虑降低波特率或加快读取）",
            SerialError.Overrun => "硬件缓冲区溢出，数据丢失",
            SerialError.RXParity => "奇偶校验错误",
            SerialError.Frame => "帧错误（波特率/校验/停止位不匹配）",
            SerialError.TXFull => "发送缓冲区已满",
            _ => e.EventType.ToString()
        };
        RaiseError("串口错误：" + friendly);
    }

    /// <summary>
    /// 将 I/O / 端口相关异常翻译为中文友好提示。
    /// </summary>
    protected static string GetFriendlyMessage(Exception ex)
    {
        if (ex == null) return string.Empty;
        if (ex is UnauthorizedAccessException)
            return "端口被占用（已被其他程序打开），请先关闭其他串口软件再重试";
        if (ex is System.IO.FileNotFoundException)
            return "指定的串口不存在（可能设备已拔出或名称错误）";
        if (ex is System.IO.IOException)
            return "I/O 错误（可能设备断开或驱动异常）：" + ex.Message;
        if (ex is ArgumentOutOfRangeException)
            return "参数无效：" + ex.Message;
        if (ex is InvalidOperationException)
            return ex.Message;
        if (ex is TimeoutException)
            return "操作超时";
        return ex.Message;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Close();
        _writeLock.Dispose();
    }
}
