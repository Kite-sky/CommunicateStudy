using System;
using System.Net;
using System.Net.Sockets;

namespace CommunicateDemo.Services;

/// <summary>
/// UDP 服务端 — 基于 Socket 的 SendTo/ReceiveFrom
/// 维护已知客户端列表，支持主动向已知客户端发送
/// </summary>
public class UdpServerWrapper : IDisposable
{
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private IPEndPoint? _listenEp;
    // 已知客户端（收到过数据的对端）—— 去重
    private readonly HashSet<string> _knownKeys = new();
    private readonly List<IPEndPoint> _knownClients = new();
    private readonly object _lock = new();
    public bool IsRunning { get; private set; }

    public event Action<byte[], IPEndPoint?, bool>? DataReceived;
    public event Action<string>? Log;
    /// <summary>最近一次收到数据的对端</summary>
    public IPEndPoint? LastClient { get; private set; }

    public async Task StartAsync(string ip, int port)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        IPAddress address = string.IsNullOrEmpty(ip) || ip == "0.0.0.0"
           ? IPAddress.Any
           : IPAddress.Parse(ip);
        _listenEp = new IPEndPoint(address, port);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Bind(new IPEndPoint(address, port));
        IsRunning = true;
        Log?.Invoke($"UDP Server 已启动，监听 {address}:{port}");

        _ = ReceiveLoopAsync(_cts.Token);
        await Task.CompletedTask;
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

                // 记录对端为已知客户端
                var sender = result.RemoteEndPoint as IPEndPoint ?? remoteEp;
                RegisterClient(sender);

                // 拷贝实际收到的数据
                int received = result.ReceivedBytes;
                var copy = new byte[received];
                Buffer.BlockCopy(buffer, 0, copy, 0, received);
                DataReceived?.Invoke(copy, sender, false);

                // 回显
                await SendToAsync(copy, sender);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            Log?.Invoke($"UDP 接收异常: {ex.Message}");
        }
    }

    /// <summary>向指定远端发送原始字节（底层）</summary>
    private async Task SendToAsync(byte[] data, IPEndPoint ep)
    {
        if (_socket == null) throw new InvalidOperationException("UDP 未启动");
        await _socket.SendToAsync(new ReadOnlyMemory<byte>(data), SocketFlags.None, ep);
    }

    public void Dispose() => Stop();
    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        try { _socket?.Close(); _socket?.Dispose(); } catch { }
        _socket = null;
        lock (_lock) { _knownClients.Clear(); _knownKeys.Clear(); LastClient = null; }
        Log?.Invoke("UDP Server 已停止");
    }

    /// <summary>获取所有已知客户端端点的副本</summary>
    public List<IPEndPoint> GetKnownClients()
    {
        lock (_lock) { return new List<IPEndPoint>(_knownClients); }
    }

    /// <summary>向最近一次通信的客户端发送</summary>
    public async Task<bool> SendToLastClientAsync(byte[] data)
    {
        IPEndPoint? target;
        lock (_lock) { target = LastClient; }
        if (target == null) return false;

        await SendToAsync(data, target);
        DataReceived?.Invoke(data, _listenEp, true); // 自收
        return true;
    }
    /// <summary>向指定客户端发送</summary>
    public async Task<bool> SendToClientAsync(byte[] data, IPEndPoint target)
    {
        await SendToAsync(data, target);
        DataReceived?.Invoke(data, _listenEp, true); // 自收
        return true;
    }
    /// <summary>向所有已知客户端广播</summary>
    public async Task BroadcastToKnownClientsAsync(byte[] data)
    {
        List<IPEndPoint> targets;
        lock (_lock) { targets = new List<IPEndPoint>(_knownClients); }

        foreach (var ep in targets)
        {
            try { await SendToAsync(data, ep); }
            catch (Exception ex) { Log?.Invoke($"向 {ep} 发送失败: {ex.Message}"); }
        }
        DataReceived?.Invoke(data, _listenEp, true); // 自收
    }

    private void RegisterClient(IPEndPoint ep)
    {
        lock (_lock)
        {
            LastClient = ep;
            var key = ep.ToString();
            if (_knownKeys.Add(key))
                _knownClients.Add(ep);
        }
    }
}
