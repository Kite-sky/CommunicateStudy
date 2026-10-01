using System.Net;
using System.Net.Sockets;

namespace CommunicateDemo.Services
{
    /// <summary>
    /// TCP 服务端封装
    /// 网络层只传 byte[]，不做任何编码/解码
    /// </summary>
    public class TcpServerWrapper : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly List<TcpClient> _clients = [];
        private readonly object _lock = new object();
        private IPEndPoint? _listenEp;
        public bool IsRuning { get; private set; }
        /// <summary>收到原始字节事件：(原始数据, 远端EP, 是否来自服务端自己发送的广播)</summary>
        public event Action<byte[], IPEndPoint?, bool>? DataReceived;
        public event Action<string>? Log;

        public async Task StartAsync(string ip, int port)
        {
            if (IsRuning) return;
            _cts = new CancellationTokenSource();
            IPAddress address = string.IsNullOrEmpty(ip) ? IPAddress.Any : IPAddress.Parse(ip);
            _listenEp = new IPEndPoint(address, port);
            _listener = new TcpListener(_listenEp);
            _listener.Start();
            IsRuning = true;
            Log?.Invoke($"TCP服务端已启动，监听 {_listenEp}");
            // 持续循环异步等待新客户端接入的后台循环
            _ = AcceptLoopAsync(_cts.Token);
            await Task.CompletedTask;
        }

        public void Stop()
        {
            if (!IsRuning) return;
            IsRuning = false;
            _cts?.Cancel();
            lock (_lock)
            {
                foreach (TcpClient client in _clients)
                {
                    try
                    {
                        client.Close();
                    }
                    catch (Exception) { }
                }
                _clients.Clear();
            }
            _listener?.Stop();
            Log?.Invoke($"Tcp Server 已停止");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener!.AcceptTcpClientAsync(token);
                    lock (_lock)
                    {
                        _clients.Add(client);
                    }
                    var ep = client.Client.RemoteEndPoint!;
                    Log?.Invoke($"新客户端连接: {ep}");
                    _ = HandleClientAsync(client, ep, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log?.Invoke($"Accept 异常: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client, EndPoint ep, CancellationToken token)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var buffer = new byte[65535];
                    while (!token.IsCancellationRequested && client.Client.Connected)
                    {
                        int read = await stream.ReadAsync(buffer, token);
                        if (read == 0) break; // 客户端关闭连接
                        // 拷贝一份避免 buffer 被复用
                        var copy = new byte[read];
                        Buffer.BlockCopy(buffer, 0, copy, 0, read);
                        DataReceived?.Invoke(copy, (IPEndPoint)ep, false);
                        // 回显：原样返回（由调用方决定是否加前缀）
                        await stream.WriteAsync(copy, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log?.Invoke($"处理客户端 {ep} 时发生异常: {ex.Message}");
            }
            finally
            {
                lock (_lock) { _ = _clients.Remove(client); }
                Log?.Invoke($"客户端断开: {ep}");
            }
        }

        /// <summary>向所有已连接客户端广播原始字节</summary>
        public async Task BroadcastAsync(byte[] data)
        {
            List<System.Net.Sockets.TcpClient> snapshot;
            lock (_lock) { snapshot = new List<System.Net.Sockets.TcpClient>(_clients); }
            foreach (var client in snapshot)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    await stream.WriteAsync(data);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"广播失败: {ex.Message}");
                }
            }
            // 触发自收事件，带上本机监听地址
            DataReceived?.Invoke(data, _listenEp, true);
        }

        /// <summary>向指定客户端定向发送</summary>
        public async Task SendToAsync(byte[] data, System.Net.Sockets.TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(data);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"定向发送失败: {ex.Message}");
            }
        }

        public void Dispose() => Stop();
    }
}
