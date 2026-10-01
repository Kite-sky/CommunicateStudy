using CommunicateDemo.Models;
using CommunicateDemo.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommunicateDemo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // ========== Server 侧 ==========
    private TcpServerWrapper? _tcpServer;
    private UdpServerWrapper? _udpServer;
    private ObservableCollection<ChatMessage> _serverMessages = new();

    // ========== Client 侧 ==========
    private TcpClientWrapper? _tcpClient;
    private UdpClientWrapper? _udpClient;
    private ObservableCollection<ChatMessage> _clientMessages = new();

    // 重连相关
    private CancellationTokenSource? _reconnectCts;
    private (bool isTcp, string ip, int port, int localPort)? _connectParams;
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            RefreshIpCombos();
            RefreshServerKnownClients(); // 先初始化 ComboBox 为空
        };
        ServerMsgList.ItemsSource = _serverMessages;
        ClientMsgList.ItemsSource = _clientMessages;

        // Server 协议切换时刷新目标 ComboBox 可见性 + 目标列表
        ServerTcpRadio.Checked += (s, e) => ToggleServerTargetVisible();
        ServerUdpRadio.Checked += (s, e) => ToggleServerTargetVisible();
    }
    /// <summary>刷新 UDP 已知客户端到 IP ComboBox（仅列出所有不同的 IP，选中时自动带端口）</summary>
    private void RefreshServerKnownClients()
    {
        InvokeUi(() =>
        {
            var known = _udpServer?.GetKnownClients() ?? new List<IPEndPoint>();
            var currentIp = ServerTargetIpCombo.Text;
            var items = known.Select(ep => ep.ToString()).Prepend("255.255.255.255").ToList();

            ServerTargetIpCombo.ItemsSource = items;
            // 保持当前输入/选中的 IP（不要被覆盖）
            if (!string.IsNullOrEmpty(currentIp) && items.Contains(currentIp))
                ServerTargetIpCombo.SelectedItem = currentIp;
            else if (items.Count > 0)
                ServerTargetIpCombo.SelectedItem = items[0];
        });
    }

    private void ToggleServerTargetVisible()
    {
        bool isUdp = ServerUdpRadio.IsChecked == true;
        InvokeUi(() =>
        {
            var v = isUdp ? Visibility.Visible : Visibility.Collapsed;
            ServerTargetLabel.Visibility = v;
            ServerTargetIpCombo.Visibility = v;
            ServerTargetPortLabel.Visibility = v;
            ServerTargetPortBox.Visibility = v;
            ServerUdpBroadcastBtn.Visibility = v;
            if (isUdp) RefreshServerKnownClients();
        });
    }

    private void RefreshIpCombos()
    {
        var ips = NetworkHelper.GetLocalIPv4Addresses();
        ServerIpCombo.ItemsSource = ips;
        ServerIpCombo.SelectedItem = "0.0.0.0";
        ClientIpCombo.ItemsSource = ips;
        ClientIpCombo.SelectedItem = "127.0.0.1";
    }

    // ============================================================
    // 帮助方法
    // ============================================================
    private void InvokeUi(Action action) => Dispatcher.Invoke(action);

    private static T GetComboEnum<T>(ComboBox cb) where T : Enum
    {
        if (cb.SelectedItem is ComboBoxItem ci && ci.Tag is string tag)
            return (T)Enum.Parse(typeof(T), tag);
        return default!;
    }
    private MessageFormat GetRxFmt(ComboBox cb) => GetComboEnum<MessageFormat>(cb);
    private MessageFormat GetTxFmt(ComboBox cb) => GetComboEnum<MessageFormat>(cb);
    private TextEncoding GetRxEnc(ComboBox cb) => GetComboEnum<TextEncoding>(cb);
    private TextEncoding GetTxEnc(ComboBox cb) => GetComboEnum<TextEncoding>(cb);


    // ============================================================
    // Server 页 — Start / Stop
    // ============================================================
    private async void ServerStartBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ip = (ServerIpCombo.Text ?? "0.0.0.0").Trim();
            var port = int.Parse(ServerPortBox.Text.Trim());

            if (ServerTcpRadio.IsChecked == true)
            {
                _tcpServer = new TcpServerWrapper();
                _tcpServer.DataReceived += (bytes, ep, self) =>
                    AppendMessage(_serverMessages, bytes, ep?.ToString() ?? "unknown", self);
                _tcpServer.Log += msg => AppendLog(ServerLogBox, msg);
                await _tcpServer.StartAsync(ip, port);
            }
            else
            {
                _udpServer = new UdpServerWrapper();
                _udpServer.DataReceived += (bytes, ep, self) =>
                    AppendMessage(_serverMessages, bytes, ep?.ToString() ?? "unknown", self);
                _udpServer.Log += msg => AppendLog(ServerLogBox, msg);
                await _udpServer.StartAsync(ip, port);
            }
            ServerStartBtn.IsEnabled = false;
            ServerStopBtn.IsEnabled = true;
        }
        catch (Exception ex) { AppendLog(ServerLogBox, $"启动失败: {ex.Message}"); }
    }

    private void AppendLog(TextBox logBox, string msg)
    {
        InvokeUi(() =>
        {
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            logBox.ScrollToEnd();
        });
        FileLogger.Write("LOG", msg);
    }

    private void AppendMessage(ObservableCollection<ChatMessage> list, byte[] raw, string sender, bool isSelf)
    {
        bool isServer = list == _serverMessages;
        var direction = isSelf ? "TX ▶" : "RX ◀";
        var ts = DateTime.Now;
        InvokeUi(() =>
        {
            var content = RenderContent(raw, isServer);
            var msg = new ChatMessage
            {
                RawData = raw,
                Sender = sender,
                IsSelf = isSelf,
                Timestamp = ts,
                Display = BuildDisplay(direction, ts, sender, content)
            };
            list.Add(msg);
            var lb = isServer ? ServerMsgList : ClientMsgList;
            if (lb.Items.Count > 0) lb.ScrollIntoView(lb.Items[lb.Items.Count - 1]);
        });

        // 日志框同步输出
        var rawHex = BitConverter.ToString(raw).Replace('-', ' ');
        var logLine = $"{direction} [{ts:HH:mm:ss.fff}] {sender,-20} {rawHex}";
        InvokeUi(() =>
        {
            var logBox = isServer ? ServerLogBox : ClientLogBox;
            logBox.AppendText(logLine + "\n");
            logBox.ScrollToEnd();
        });
        FileLogger.Write(isSelf ? "TX" : "RX", $"[{sender}] {rawHex}");
    }

    /// <summary>拼完整 Display = 头部 + 内容</summary>
    private string BuildDisplay(string direction, DateTime ts, string sender, object content)
    {
        return $"{direction} [{ts:HH:mm:ss.fff}] {sender,-22} {content}";
    }

    /// <summary>仅渲染消息内容部分（不含头部）</summary>
    private string RenderContent(byte[] raw, bool isServer)
    {
        MessageFormat fmt; TextEncoding enc;
        if (isServer)
        {
            fmt = GetRxFmt(ServerRxFmtCombo);
            enc = GetRxEnc(ServerRxEncCombo);
        }
        else
        {
            fmt = GetRxFmt(ClientRxFmtCombo);
            enc = GetRxEnc(ClientRxEncCombo);
        }
        return MessageFormatter.BytesToString(raw, fmt, enc);
    }

    private void ServerStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _tcpServer?.Stop(); _tcpServer = null;
        _udpServer?.Stop(); _udpServer = null;
        ServerStartBtn.IsEnabled = true; ServerStopBtn.IsEnabled = false;
    }

    // ============================================================
    // 接收格式变化 → 重新渲染所有消息内容部分
    // ============================================================
    private void ServerFmt_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        InvokeUi(() =>
        {
            foreach (var m in _serverMessages)
            {
                var dir = m.IsSelf ? "TX ▶" : "RX ◀";
                m.Display = BuildDisplay(dir, m.Timestamp, m.Sender, RenderContent(m.RawData, true));
            }
        });
    }

    // ============================================================
    // Server 页 — 发送 / 清空 / 导出
    // ============================================================
    private void ServerSendBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ServerSendBtn_Click(sender, e); e.Handled = true; }
    }

    private async void ServerSendBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = ServerSendBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var fmt = GetTxFmt(ServerTxFmtCombo);
            var enc = GetTxEnc(ServerTxEncCombo);
            var data = MessageFormatter.StringToBytes(text, fmt, enc);

            if (_tcpServer != null)
            {
                await _tcpServer.BroadcastAsync(data);
            }
            else if (_udpServer != null)
            {
                // UDP 无连接，发给最近一次通信的客户端；若有多个已知客户端则广播
                var known = _udpServer.GetKnownClients();
                if (known.Count == 0)
                {
                    AppendLog(ServerLogBox, "⚠️ UDP Server 暂无已知客户端，请先等客户端发一条消息");
                    return;
                }
                if (known.Count == 1)
                {
                    await _udpServer.SendToLastClientAsync(data);
                    AppendLog(ServerLogBox, $"已向 {_udpServer.LastClient} 发送");
                }
                else
                {
                    await _udpServer.BroadcastToKnownClientsAsync(data);
                    AppendLog(ServerLogBox, $"已向 {known.Count} 个已知客户端广播");
                }
            }
            else { AppendLog(ServerLogBox, "请先 Start Server"); return; }

            ServerSendBox.Clear();
        }
        catch (Exception ex) { AppendLog(ServerLogBox, $"发送失败: {ex.Message}"); }
    }

    private void ServerClearBtn_Click(object sender, RoutedEventArgs e) => _serverMessages.Clear();

    private void ServerExportMsg_Click(object sender, RoutedEventArgs e) => ExportList(_serverMessages, "ServerMessages");

    // ============================================================
    // 导出辅助
    // ============================================================
    private void ExportList(ObservableCollection<ChatMessage> list, string defaultName)
    {
        if (list.Count == 0) { MessageBox.Show("没有可导出的消息", "提示"); return; }
        var sfd = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"{defaultName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (sfd.ShowDialog() == true)
        {
            var lines = list.Select(m =>
                $"[{m.Timestamp:HH:mm:ss.fff}] {(m.IsSelf ? "TX" : "RX")} [{m.Sender}] 0x{BitConverter.ToString(m.RawData).Replace("-", "")} | {m.Display}");
            File.WriteAllLines(sfd.FileName, lines);
            MessageBox.Show($"已导出 {list.Count} 条消息到:\n{sfd.FileName}", "导出成功");
        }
    }

    private void ServerExportLog_Click(object sender, RoutedEventArgs e) => ExportTextBox(ServerLogBox, "ServerLog");

    private void ExportTextBox(TextBox tb, string defaultName)
    {
        if (string.IsNullOrWhiteSpace(tb.Text)) { MessageBox.Show("没有可导出的日志", "提示"); return; }
        var sfd = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"{defaultName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (sfd.ShowDialog() == true)
        {
            File.WriteAllText(sfd.FileName, tb.Text);
            MessageBox.Show($"已导出日志到:\n{sfd.FileName}", "导出成功");
        }
    }

    private void ClientTcpRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ClientLocalPortBox != null) ClientLocalPortBox.Visibility = Visibility.Collapsed;
        if (ClientLocalPortLabel != null) ClientLocalPortLabel.Visibility = Visibility.Collapsed;
    }

    private void ClientUdpRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ClientLocalPortBox != null) ClientLocalPortBox.Visibility = Visibility.Visible;
        if (ClientLocalPortLabel != null) ClientLocalPortLabel.Visibility = Visibility.Visible;
    }
    // ============================================================
    // Client 页 — Connect（含保存参数和注册重连）
    // ============================================================
    private void ClientDisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        StopReconnectLoop();
        if (_tcpClient != null)
        {
            _tcpClient.Disconnected -= onTcpDisconnected;
            _tcpClient.Disconnect();
            _tcpClient = null;
        }
        if (_udpClient != null)
        {
            _udpClient.Disconnected -= onUdpDisconnected;
            _udpClient.Stop();
            _udpClient = null;
        }
        _connectParams = null;
        ClientConnectBtn.IsEnabled = true; ClientDisconnectBtn.IsEnabled = false;
    }

    private void ClientFmt_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        InvokeUi(() =>
        {
            foreach (var m in _clientMessages)
            {
                var dir = m.IsSelf ? "TX ▶" : "RX ◀";
                m.Display = BuildDisplay(dir, m.Timestamp, m.Sender, RenderContent(m.RawData, false));
            }
        });
    }

    private void ClientSendBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ClientSendBtn_Click(sender, e); e.Handled = true; }
    }

    private async void ClientSendBtn_Click(object sender, RoutedEventArgs e)
    {
        var text = ClientSendBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var fmt = GetTxFmt(ClientTxFmtCombo);
            var enc = GetTxEnc(ClientTxEncCombo);
            var data = MessageFormatter.StringToBytes(text, fmt, enc);

            if (_tcpClient != null) await _tcpClient.SendAsync(data);
            else if (_udpClient != null) await _udpClient.SendAsync(data);
            else { AppendLog(ClientLogBox, "请先 Connect"); return; }

            ClientSendBox.Clear();
        }
        catch (Exception ex) { AppendLog(ClientLogBox, $"发送失败: {ex.Message}"); }
    }

    private void ClientClearBtn_Click(object sender, RoutedEventArgs e) => _clientMessages.Clear();

    private void ClientExportMsg_Click(object sender, RoutedEventArgs e) => ExportList(_clientMessages, "ClientMessages");

    private void ClientExportLog_Click(object sender, RoutedEventArgs e) => ExportTextBox(ClientLogBox, "ClientLog");

    private async void ClientConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool isTcp = ClientTcpRadio.IsChecked == true;
            var remoteIp = (ClientIpCombo.Text ?? "127.0.0.1").Trim();
            var remotePort = int.Parse(ClientPortBox.Text.Trim());
            int localPort = isTcp ? 0 : int.Parse(ClientLocalPortBox.Text.Trim());

            // 先取消之前的重连
            StopReconnectLoop();

            if (isTcp)
            {
                _tcpClient = new TcpClientWrapper();
                _tcpClient.DataReceived += (bytes, ep, self) =>
                    AppendMessage(_clientMessages, bytes, ep?.ToString() ?? "TCP", self);
                _tcpClient.Log += msg => AppendLog(ClientLogBox, msg);
                _tcpClient.Disconnected += onTcpDisconnected;
                await _tcpClient.ConnectAsync(remoteIp, remotePort);
            }
            else
            {
                _udpClient = new UdpClientWrapper();
                _udpClient.DataReceived += (bytes, ep, self) =>
                    AppendMessage(_clientMessages, bytes, ep?.ToString() ?? "UDP", self);
                _udpClient.Log += msg => AppendLog(ClientLogBox, msg);
                _udpClient.Disconnected += onUdpDisconnected;
                await _udpClient.StartAsync(localPort, remoteIp, remotePort);
            }

            // 保存参数用于重连
            _connectParams = (isTcp, remoteIp, remotePort, localPort);
            ClientConnectBtn.IsEnabled = false;
            ClientDisconnectBtn.IsEnabled = true;
        }
        catch (Exception ex) { AppendLog(ClientLogBox, $"连接失败: {ex.Message}"); }
    }

    private void onUdpDisconnected(bool intentional)
    {
        InvokeUi(() =>
        {
            ClientConnectBtn.IsEnabled = true; ClientDisconnectBtn.IsEnabled = false;
        });
        if (!intentional && ClientAutoReconnectCheck.IsChecked == true)
        {
            StartReconnectLoop();
        }
    }

    private void onTcpDisconnected(bool intentional)
    {
        InvokeUi(() =>
        {
            ClientConnectBtn.IsEnabled = true; ClientDisconnectBtn.IsEnabled = false;
        });
        if (!intentional && ClientAutoReconnectCheck.IsChecked == true)
        {
            StartReconnectLoop();
        }
    }

    private void StartReconnectLoop()
    {
        if (_connectParams == null) return;
        if (_reconnectCts != null && !_reconnectCts.IsCancellationRequested) return;

        _reconnectCts = new CancellationTokenSource();
        var ct = _reconnectCts.Token;
        var p = _connectParams.Value;

        AppendLog(ClientLogBox, $"🔄 连接断开，{p.ip}:{p.port} 开始自动重连...");

        _ = Task.Run(async () =>
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                attempt++;
                try
                {
                    await Task.Delay(Math.Min(2000 + attempt * 1000, 15000), ct);

                    InvokeUi(() => AppendLog(ClientLogBox, $"重连第 {attempt} 次 → {p.ip}:{p.port}"));

                    if (p.isTcp)
                    {
                        var client = new TcpClientWrapper();
                        bool success = false;
                        try
                        {
                            await client.ConnectAsync(p.ip, p.port);
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            InvokeUi(() => AppendLog(ClientLogBox, $"重连失败: {ex.Message}"));
                            client.Dispose();
                        }
                        if (success)
                        {
                            // 替换旧实例
                            _tcpClient?.Disconnect();
                            _tcpClient = client;
                            _tcpClient.DataReceived += (bytes, ep, self) =>
                                AppendMessage(_clientMessages, bytes, ep?.ToString() ?? "TCP", self);
                            _tcpClient.Log += msg => AppendLog(ClientLogBox, msg);
                            _tcpClient.Disconnected += onTcpDisconnected;
                            InvokeUi(() =>
                            {
                                ClientConnectBtn.IsEnabled = false;
                                ClientDisconnectBtn.IsEnabled = true;
                            });
                            AppendLog(ClientLogBox, "✅ 重连成功！");
                            return;
                        }
                    }
                    else
                    {
                        var client = new UdpClientWrapper();
                        bool success = false;
                        try
                        {
                            await client.StartAsync(p.localPort, p.ip, p.port);
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            InvokeUi(() => AppendLog(ClientLogBox, $"重连失败: {ex.Message}"));
                            client.Dispose();
                        }
                        if (success)
                        {
                            _udpClient?.Stop();
                            _udpClient = client;
                            _udpClient.DataReceived += (bytes, ep, self) =>
                                AppendMessage(_clientMessages, bytes, ep?.ToString() ?? "UDP", self);
                            _udpClient.Log += msg => AppendLog(ClientLogBox, msg);
                            _udpClient.Disconnected += onUdpDisconnected;
                            InvokeUi(() =>
                            {
                                ClientConnectBtn.IsEnabled = false;
                                ClientDisconnectBtn.IsEnabled = true;
                            });
                            AppendLog(ClientLogBox, "✅ UDP 重连成功！");
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    // ============================================================
    // 窗口关闭
    // ============================================================
    protected override void OnClosed(EventArgs e)
    {
        StopReconnectLoop();
        _tcpServer?.Stop(); _udpServer?.Stop();
        _tcpClient?.Disconnect(); _udpClient?.Stop();
        base.OnClosed(e);
    }

    private void StopReconnectLoop()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = null;
    }

    private void ServerTargetIpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerTargetIpCombo.SelectedItem is string selected && selected.Contains(':'))
        {
            var parts = selected.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                ServerTargetIpCombo.Text = parts[0];
                ServerTargetPortBox.Text = port.ToString();
            }
        }
    }

    private async void ServerUdpBroadcastBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_udpServer == null) { AppendLog(ServerLogBox, "请先 Start UDP Server"); return; }
        var text = ServerSendBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            var fmt = GetTxFmt(ServerTxFmtCombo);
            var enc = GetTxEnc(ServerTxEncCombo);
            var data = MessageFormatter.StringToBytes(text, fmt, enc);

            var known = _udpServer.GetKnownClients();
            if (known.Count == 0)
            {
                // 广播到 LAN 广播地址
                var broadcastEp = new IPEndPoint(IPAddress.Broadcast,
                    int.TryParse(ServerTargetPortBox.Text, out var p) && p > 0 ? p : 8080);
                await _udpServer.SendToClientAsync(data, broadcastEp);
                AppendLog(ServerLogBox, $"📢 广播到 LAN {broadcastEp}");
            }
            else
            {
                await _udpServer.BroadcastToKnownClientsAsync(data);
                AppendLog(ServerLogBox, $"📢 已广播到 {known.Count} 个已知客户端");
            }

            ServerSendBox.Clear();
        }
        catch (Exception ex) { AppendLog(ServerLogBox, $"广播失败: {ex.Message}"); }
    }
}