using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CommunicateDemo.Services;

/// <summary>
/// TCP 客户端封装 — 网络层只传 byte[]
/// </summary>
public class TcpClientWrapper : IDisposable
{
    private System.Net.Sockets.TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;

    public bool IsConnected { get; private set; }

    /// <summary>原始字节数据事件</summary>
    public event Action<byte[], IPEndPoint?, bool>? DataReceived;
    public event Action<string>? Log;
    /// <summary>连接意外断开事件（参数=是否主动断开）</summary>
    public event Action<bool>? Disconnected;

    public async Task ConnectAsync(string ip, int port)
    {
        if (IsConnected) return;

        _client = new System.Net.Sockets.TcpClient();
        await _client.ConnectAsync(ip, port);
        _stream = _client.GetStream();
        _cts = new CancellationTokenSource();
        IsConnected = true;

        var ep = _client.Client.RemoteEndPoint as IPEndPoint;
        Log?.Invoke($"已连接到 TCP Server {ep}");

        // `_` 是弃元（discard），意思：我调用这个异步方法，但不接收它返回的 `Task`，不等待它，也不保存变量。
        _ = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>发送原始字节</summary>
    public async Task SendAsync(byte[] data)
    {
        if (!IsConnected || _stream == null || _client == null)
            throw new InvalidOperationException("未连接");
        await _stream.WriteAsync(data);
        var remoteEp = (IPEndPoint)_client.Client.RemoteEndPoint!;
        DataReceived?.Invoke(data, remoteEp, true); // 自收：带远端地址
    }

    public void Disconnect()
    {
        if (!IsConnected) return;
        IsConnected = false;
        _cts?.Cancel();
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        Log?.Invoke("TCP 客户端已断开");
        Disconnected?.Invoke(true); // 主动
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[65536];
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int read = await _stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                var copy = new byte[read];
                Buffer.BlockCopy(buffer, 0, copy, 0, read);
                DataReceived?.Invoke(copy, null, false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log?.Invoke($"接收异常: {ex.Message}");
        }
        finally
        {
            bool wasActive = IsConnected;
            IsConnected = false;
            if (wasActive && !ct.IsCancellationRequested)
            {
                Log?.Invoke("连接已断开");
                Disconnected?.Invoke(false); // 意外断开
            }
        }
    }

    public void Dispose() => Disconnect();
}
