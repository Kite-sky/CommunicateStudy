using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicateDemo.Services;

/// <summary>
/// UDP 客户端 — 基于 Socket 的 SendTo/ReceiveFrom
/// 支持定向发送、广播、自定义目标
/// </summary>
public class UdpClientWrapper : IDisposable
{
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private IPEndPoint? _remoteEp;
    private IPEndPoint? _localEp;

    public bool IsRunning { get; private set; }
    public int LocalPort => (_localEp)?.Port ?? 0;

    public event Action<byte[], IPEndPoint?, bool>? DataReceived;
    public event Action<string>? Log;
    /// <summary>接收循环意外终止事件</summary>
    public event Action<bool>? Disconnected;

    /// <summary>
    /// 初始化 UDP 客户端
    /// </summary>
    public async Task StartAsync(int localPort, string remoteIp, int remotePort)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        _localEp = new IPEndPoint(IPAddress.Any, localPort);
        _socket.Bind(_localEp);

        _remoteEp = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);
        IsRunning = true;

        Log?.Invoke($"UDP Client 已启动（本地端口:{LocalPort}），目标 {_remoteEp}");
        _ = ReceiveLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    /// <summary>发送原始字节（默认发给 Connect 时指定的远端）— 使用 Socket.SendTo</summary>
    public async Task SendAsync(byte[] data)
    {
        if (!IsRunning || _socket == null || _remoteEp == null)
            throw new InvalidOperationException("UDP 客户端未启动");
        await _socket.SendToAsync(new ReadOnlyMemory<byte>(data), SocketFlags.None, _remoteEp);
        DataReceived?.Invoke(data, _remoteEp, true);
    }

    /// <summary>发送原始字节到指定目标 IP:Port（支持广播和任意目标）— 使用 Socket.SendTo</summary>
    public async Task SendToAsync(byte[] data, IPEndPoint target)
    {
        if (!IsRunning || _socket == null)
            throw new InvalidOperationException("UDP 客户端未启动");
        await _socket.SendToAsync(new ReadOnlyMemory<byte>(data), SocketFlags.None, target);
        DataReceived?.Invoke(data, target, true);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        try { _socket?.Close(); _socket?.Dispose(); } catch { }
        _socket = null;
        Log?.Invoke("UDP Client 已停止");
        Disconnected?.Invoke(true);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (!ct.IsCancellationRequested && _socket != null)
            {
                // 使用 Socket.ReceiveFromAsync — 标准 UDP recvfrom
                var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                var result = await _socket.ReceiveFromAsync(
                    new Memory<byte>(buffer), SocketFlags.None, remoteEp, ct);

                var sender = result.RemoteEndPoint as IPEndPoint ?? remoteEp;
                int received = result.ReceivedBytes;
                var copy = new byte[received];
                Buffer.BlockCopy(buffer, 0, copy, 0, received);
                DataReceived?.Invoke(copy, sender, false);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Log?.Invoke($"UDP 接收异常: {ex.Message}");
        }
        finally
        {
            bool wasActive = IsRunning;
            IsRunning = false;
            if (wasActive && !ct.IsCancellationRequested)
            {
                Log?.Invoke("UDP 接收循环已终止");
                Disconnected?.Invoke(false);
            }
        }
    }

    public void Dispose() => Stop();
}
