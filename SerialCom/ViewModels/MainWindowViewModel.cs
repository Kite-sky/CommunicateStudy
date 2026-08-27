using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Mvvm;
using SerialCom.Models;
using SerialCom.Services;
using DataFormat = SerialCom.Models.DataFormat;
using ErrorEventArgs = SerialCom.Services.ErrorEventArgs;

namespace SerialCom.ViewModels;

/// <summary>
/// 主窗口视图模型：负责串口配置、收发控制与日志展示。
/// </summary>
public class MainWindowViewModel : BindableBase, IDisposable
{
    private readonly ISerialServiceFactory _factory;
    private readonly ISettingsService _settings;

    private ISerialService? _service;
    private bool _isOpen;
    private string _sendText = string.Empty;
    private long _txCount;
    private long _rxCount;
    private bool _autoScrollLog = true;
    private int _logMaxLines = 10000;

    // 选中项
    private string? _selectedPort;
    private int _selectedBaudRate = 9600;
    private int _selectedDataBits = 8;
    private Parity _selectedParity = Parity.None;
    private StopBits _selectedStopBits = StopBits.One;
    private Handshake _selectedHandshake = Handshake.None;
    private CommunicationMode _selectedMode = CommunicationMode.Rs232;
    private DataFormat _selectedFormat = DataFormat.Ascii;
    private string _selectedEncoding = "us-ascii";
    private bool _rtsHighWhenTransmitting = true;
    private bool _appendNewLineOnSend = true;

    // 周期发送
    private bool _autoSendEnabled;
    private int _autoSendIntervalMs = 1000;
    private string _autoSendText = string.Empty;
    private DispatcherTimer? _autoSendTimer;
    private long _autoSendCounter;

    // 周期发送防重入：上一轮尚未完成时跳过本轮 Tick（发送间隔可能短于实际发送耗时）
    private int _autoSendInFlight;

    // ASCII 模式接收端的跨包解码器（解决多字节字符被 DataReceived 事件切成两半导致的半字乱码）
    private Decoder? _rxDecoder;
    private readonly object _rxDecoderLock = new();

    public MainWindowViewModel(ISerialServiceFactory factory, ISettingsService settings)
    {
        _factory = factory;
        _settings = settings;

        RefreshPortsCommand = new DelegateCommand(RefreshPorts);
        OpenPortCommand = new DelegateCommand(OpenPort, () => !IsOpen && !string.IsNullOrEmpty(SelectedPort))
            .ObservesProperty(() => IsOpen)
            .ObservesProperty(() => SelectedPort);
        ClosePortCommand = new DelegateCommand(ClosePort, () => IsOpen)
            .ObservesProperty(() => IsOpen);
        SendCommand = new DelegateCommand(SendData, () => IsOpen && !string.IsNullOrEmpty(SendText))
            .ObservesProperty(() => IsOpen)
            .ObservesProperty(() => SendText);
        ClearLogCommand = new DelegateCommand(() => Log.Clear());
        ExportLogCommand = new DelegateCommand(ExportLog);

        BaudRates = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
        DataBitsOptions = new[] { 5, 6, 7, 8 };
        ParityOptions = new[] { Parity.None, Parity.Odd, Parity.Even, Parity.Mark, Parity.Space };
        StopBitsOptions = new[] { StopBits.One, StopBits.OnePointFive, StopBits.Two };
        HandshakeOptions = new[] { Handshake.None, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff, Handshake.XOnXOff };
        ModeOptions = new[] { CommunicationMode.Rs232, CommunicationMode.Rs485 };
        FormatOptions = new[] { DataFormat.Ascii, DataFormat.Hex };
        EncodingOptions = new[]
        {
            new EncodingOption("ASCII", "us-ascii"),
            new EncodingOption("UTF-8", "utf-8"),
            new EncodingOption("GB2312 / 中文", "gb2312"),
            new EncodingOption("Latin-1 / ISO-8859-1", "iso-8859-1")
        };

        LoadSettings();
        RefreshPorts();
    }

    #region 选项集合

    public ObservableCollection<string> AvailablePorts { get; } = new();
    public int[] BaudRates { get; }
    public int[] DataBitsOptions { get; }
    public Parity[] ParityOptions { get; }
    public StopBits[] StopBitsOptions { get; }
    public Handshake[] HandshakeOptions { get; }
    public CommunicationMode[] ModeOptions { get; }
    public DataFormat[] FormatOptions { get; }
    public EncodingOption[] EncodingOptions { get; }

    #endregion

    #region 选中项

    public string? SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
    }

    public int SelectedDataBits
    {
        get => _selectedDataBits;
        set => SetProperty(ref _selectedDataBits, value);
    }

    public Parity SelectedParity
    {
        get => _selectedParity;
        set => SetProperty(ref _selectedParity, value);
    }

    public StopBits SelectedStopBits
    {
        get => _selectedStopBits;
        set => SetProperty(ref _selectedStopBits, value);
    }

    public Handshake SelectedHandshake
    {
        get => _selectedHandshake;
        set => SetProperty(ref _selectedHandshake, value);
    }

    public CommunicationMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                RaisePropertyChanged(nameof(IsRs485));
                RaisePropertyChanged(nameof(CanEditHandshake));
            }
        }
    }

    public DataFormat SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }

    public string SelectedEncoding
    {
        get => _selectedEncoding;
        set => SetProperty(ref _selectedEncoding, value);
    }

    public bool RtsHighWhenTransmitting
    {
        get => _rtsHighWhenTransmitting;
        set => SetProperty(ref _rtsHighWhenTransmitting, value);
    }

    /// <summary>RS-485 才显示 RTS 极性选项。</summary>
    public bool IsRs485 => SelectedMode == CommunicationMode.Rs485;

    /// <summary>RS-485 模式下禁用硬件握手（由 RTS 手动控制方向）。</summary>
    public bool CanEditHandshake => SelectedMode != CommunicationMode.Rs485;

    public bool AppendNewLineOnSend
    {
        get => _appendNewLineOnSend;
        set => SetProperty(ref _appendNewLineOnSend, value);
    }

    public bool AutoScrollLog
    {
        get => _autoScrollLog;
        set => SetProperty(ref _autoScrollLog, value);
    }

    public int LogMaxLines
    {
        get => _logMaxLines;
        set => SetProperty(ref _logMaxLines, value);
    }

    // --- 周期发送 ---
    public bool AutoSendEnabled
    {
        get => _autoSendEnabled;
        set
        {
            if (SetProperty(ref _autoSendEnabled, value))
            {
                if (value) StartAutoSendTimer();
                else StopAutoSendTimer();
            }
        }
    }

    public int AutoSendIntervalMs
    {
        get => _autoSendIntervalMs;
        set
        {
            if (value < 1) value = 1;
            if (SetProperty(ref _autoSendIntervalMs, value) && _autoSendTimer != null)
            {
                _autoSendTimer.Interval = TimeSpan.FromMilliseconds(value);
            }
        }
    }

    public string AutoSendText
    {
        get => _autoSendText;
        set => SetProperty(ref _autoSendText, value);
    }

    public long AutoSendCounter
    {
        get => _autoSendCounter;
        private set => SetProperty(ref _autoSendCounter, value);
    }

    #endregion

    #region 状态与数据

    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value))
            {
                RaisePropertyChanged(nameof(IsClosed));
                if (!value) StopAutoSendTimer();
            }
        }
    }

    public bool IsClosed => !IsOpen;

    public string SendText
    {
        get => _sendText;
        set => SetProperty(ref _sendText, value);
    }

    public ObservableCollection<LogEntry> Log { get; } = new();

    public long TxCount
    {
        get => _txCount;
        private set => SetProperty(ref _txCount, value);
    }

    public long RxCount
    {
        get => _rxCount;
        private set => SetProperty(ref _rxCount, value);
    }

    #endregion

    #region 命令

    public DelegateCommand RefreshPortsCommand { get; }
    public DelegateCommand OpenPortCommand { get; }
    public DelegateCommand ClosePortCommand { get; }
    public DelegateCommand SendCommand { get; }
    public DelegateCommand ClearLogCommand { get; }
    public DelegateCommand ExportLogCommand { get; }

    #endregion

    #region 操作实现

    private void LoadSettings()
    {
        var s = _settings.Load();
        SelectedMode = s.Mode;
        SelectedBaudRate = s.BaudRate;
        SelectedDataBits = s.DataBits;
        SelectedParity = s.Parity;
        SelectedStopBits = s.StopBits;
        SelectedHandshake = s.Handshake;
        RtsHighWhenTransmitting = s.RtsHighWhenTransmitting;
        SelectedFormat = s.DataFormat;
        SelectedEncoding = string.IsNullOrEmpty(s.TextEncodingName) ? "us-ascii" : s.TextEncodingName;
        AppendNewLineOnSend = s.AppendNewLineOnSend;
        AutoScrollLog = s.AutoScrollLog;
        LogMaxLines = (int)(s.LogMaxLines > 0 ? s.LogMaxLines : 10000);

        AutoSendEnabled = s.AutoSendEnabled;
        AutoSendIntervalMs = s.AutoSendIntervalMs;
        AutoSendText = s.AutoSendText ?? string.Empty;
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(new AppSettings
            {
                Mode = SelectedMode,
                PortName = SelectedPort ?? string.Empty,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                Parity = SelectedParity,
                StopBits = SelectedStopBits,
                Handshake = SelectedHandshake,
                RtsHighWhenTransmitting = RtsHighWhenTransmitting,
                DataFormat = SelectedFormat,
                TextEncodingName = SelectedEncoding,
                AppendNewLineOnSend = AppendNewLineOnSend,
                AutoScrollLog = AutoScrollLog,
                LogMaxLines = LogMaxLines,
                AutoSendEnabled = AutoSendEnabled,
                AutoSendIntervalMs = AutoSendIntervalMs,
                AutoSendText = AutoSendText ?? string.Empty
            });
        }
        catch
        {
            // 忽略，不影响主流程
        }
    }

    private void RefreshPorts()
    {
        AvailablePorts.Clear();
        var s = _settings.Load();
        foreach (var p in _factory.Create(SelectedMode).GetAvailablePorts())
        {
            AvailablePorts.Add(p);
        }
        if (!string.IsNullOrEmpty(s.PortName))
        {
            // 选择上次保存的端口（若存在）
            if (AvailablePorts.Contains(s.PortName))
            {
                SelectedPort = s.PortName;
                return;
            }
        }
        if (SelectedPort == null && AvailablePorts.Count > 0)
        {
            SelectedPort = AvailablePorts[0];
        }
    }

    private void OpenPort()
    {
        try
        {
            _service = _factory.Create(SelectedMode);
            _service.DataReceived += OnDataReceived;
            _service.ErrorOccurred += OnErrorOccurred;
            _service.StateChanged += OnStateChanged;

            var encoding = ResolveEncoding(SelectedEncoding);
            var config = new SerialConfig
            {
                PortName = SelectedPort!,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                Parity = SelectedParity,
                StopBits = SelectedStopBits,
                Handshake = SelectedMode == CommunicationMode.Rs485 ? Handshake.None : SelectedHandshake,
                RtsHighWhenTransmitting = RtsHighWhenTransmitting,
                TextEncoding = encoding,
                TextEncodingName = SelectedEncoding
            };

            _service.Open(config);
            IsOpen = _service.IsOpen;
            TxCount = 0;
            RxCount = 0;
            ResetRxDecoder(encoding); // 每次重开端口重置解码器状态

            AppendLog("SYS", $"已打开 {SelectedPort} @ {SelectedBaudRate} bps，模式 {SelectedMode}，编码 {encoding.WebName}");
            SaveSettings();

            if (AutoSendEnabled) StartAutoSendTimer();
        }
        catch (Exception ex)
        {
            AppendLog("ERR", ex.Message);
            DetachService();
            IsOpen = false;
        }
    }

    private void ClosePort()
    {
        StopAutoSendTimer();
        SaveSettings();
        if (_service == null)
        {
            return;
        }
        try
        {
            _service.Close();
            AppendLog("SYS", $"已关闭 {SelectedPort}");
        }
        catch (Exception ex)
        {
            AppendLog("ERR", ex.Message);
        }
        finally
        {
            DetachService();
            IsOpen = false;
        }
    }

    private async void SendData()
    {
        if (_service == null || !_service.IsOpen)
        {
            return;
        }
        try
        {
            await InternalSendAsync(SendText, isAuto: false);
        }
        catch (Exception ex)
        {
            AppendLog("ERR", "发送失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 执行实际发送（手动/自动都走这里）。
    /// 组包在 UI 线程完成，真正的 I/O 与 RS-485 等待由服务层卸载到线程池，
    /// 等待期间 UI 不冻结；await 后回到 UI 线程更新日志与计数。
    /// </summary>
    private async Task InternalSendAsync(string text, bool isAuto)
    {
        if (string.IsNullOrEmpty(text)) return;

        byte[] data;
        var format = SelectedFormat;
        if (format == DataFormat.Hex)
        {
            if (!TryParseHex(text, out data))
            {
                if (!isAuto) AppendLog("ERR", "HEX 格式不正确，请使用类似 \"A1 B2 0C\" 的写法。");
                return;
            }
            // HEX 模式为二进制帧透传，禁止附加 CRLF（否则会破坏 Modbus RTU 等协议帧）
        }
        else
        {
            var encoding = ResolveEncoding(SelectedEncoding);
            data = encoding.GetBytes(text);
            // ASCII 模式才按勾选附加换行符
            if (AppendNewLineOnSend)
            {
                var withCrLf = new byte[data.Length + 2];
                Buffer.BlockCopy(data, 0, withCrLf, 0, data.Length);
                withCrLf[data.Length] = 0x0D;
                withCrLf[data.Length + 1] = 0x0A;
                data = withCrLf;
            }
        }

        try
        {
            int sent = await _service!.SendAsync(data);
            TxCount = _service is SerialServiceBase b ? b.BytesSent : TxCount + sent;
            // 发送日志：按编码 + 格式显示
            AppendLog(isAuto ? "TX(auto)" : "TX",
                      FormatBytesForDisplay(data, format, ResolveEncoding(SelectedEncoding), asTx: true));
        }
        catch (OperationCanceledException)
        {
            AppendLog("SYS", "发送已取消");
        }
        catch (Exception ex)
        {
            AppendLog("ERR", "发送失败：" + ex.Message);
        }
    }

    private void ExportLog()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"SerialComLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var sb = new StringBuilder(capacity: Log.Count * 40);
            foreach (var e in Log)
            {
                sb.Append(e.ToString()).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            AppendLog("SYS", "日志已导出到：" + path);
        }
        catch (Exception ex)
        {
            AppendLog("ERR", "导出日志失败：" + ex.Message);
        }
    }

    #endregion

    #region 周期发送 Timer

    private void StartAutoSendTimer()
    {
        if (!IsOpen || !AutoSendEnabled || string.IsNullOrEmpty(AutoSendText))
        {
            return;
        }
        if (_autoSendTimer == null)
        {
            _autoSendTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, AutoSendIntervalMs))
            };
            _autoSendTimer.Tick += OnAutoSendTimerTick;
        }
        _autoSendTimer.Start();
        AppendLog("SYS", $"周期发送已启用：每 {AutoSendIntervalMs} ms 发送一次");
    }

    private void StopAutoSendTimer()
    {
        if (_autoSendTimer != null)
        {
            if (_autoSendTimer.IsEnabled)
            {
                _autoSendTimer.Stop();
                AppendLog("SYS", "周期发送已停止");
            }
            _autoSendTimer.Tick -= OnAutoSendTimerTick;
            _autoSendTimer = null;
        }
    }

    private async void OnAutoSendTimerTick(object? sender, EventArgs e)
    {
        if (!IsOpen || string.IsNullOrEmpty(AutoSendText)) return;
        // 上一轮未完成则跳过本轮，避免发送间隔短于实际耗时导致请求堆积
        //（服务内部另有 SemaphoreSlim 兜底串行化）
        if (Interlocked.CompareExchange(ref _autoSendInFlight, 1, 0) != 0) return;
        try
        {
            await InternalSendAsync(AutoSendText, isAuto: true);
            AutoSendCounter++;
        }
        finally
        {
            Interlocked.Exchange(ref _autoSendInFlight, 0);
        }
    }

    #endregion

    #region 事件处理（工作线程 → UI 线程）

    private void OnDataReceived(object? sender, DataReceivedEventArgs e)
    {
        var bytes = e.Data;
        long count = bytes.Length;
        var format = SelectedFormat;
        var encoding = ResolveEncoding(SelectedEncoding);

        // HEX 模式直接按字节格式化；ASCII 模式通过持续解码器增量解码（避免分包半字乱码）
        string display;
        if (format == DataFormat.Hex)
        {
            display = FormatBytesForDisplay(bytes, DataFormat.Hex, encoding, asTx: false);
        }
        else
        {
            // 确保编码对应解码器：若用户切换了编码，则重建 decoder
            EnsureRxDecoder(encoding);
            display = DecodeBytesForDisplay(bytes);
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // 优先使用服务内部计数（更准确）
            if (_service is SerialServiceBase b)
                RxCount = b.BytesReceived;
            else
                RxCount += count;
            AppendLog("RX", display);
        });
    }

    private void OnErrorOccurred(object? sender, ErrorEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => AppendLog("ERR", e.Message));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsOpen = _service?.IsOpen ?? false;
        });
    }

    #endregion

    #region 工具

    /// <summary>窗口关闭 / 应用退出时由 View 调用。</summary>
    public void Shutdown() => Dispose();

    public void Dispose()
    {
        StopAutoSendTimer();
        SaveSettings();
        DetachService();
    }

    private void DetachService()
    {
        if (_service == null)
        {
            return;
        }
        _service.DataReceived -= OnDataReceived;
        _service.ErrorOccurred -= OnErrorOccurred;
        _service.StateChanged -= OnStateChanged;
        _service.Dispose();
        _service = null;
    }

    private void AppendLog(string direction, string text)
    {
        var entry = new LogEntry { Direction = direction, Text = text };
        Log.Add(entry);

        if (Log.Count > LogMaxLines && LogMaxLines > 0)
        {
            int remove = Math.Min(200, Log.Count - LogMaxLines);
            for (int i = 0; i < remove; i++)
            {
                Log.RemoveAt(0);
            }
        }
    }

    private static string FormatBytesForDisplay(byte[] data, DataFormat format, Encoding encoding, bool asTx)
    {
        if (data == null || data.Length == 0)
        {
            return string.Empty;
        }
        if (format == DataFormat.Hex)
        {
            return BitConverter.ToString(data).Replace('-', ' ');
        }
        // ASCII 模式：用当前选定编码完整解码后，对控制字符做转义显示
        string raw = encoding.GetString(data);
        return EscapeForDisplay(raw);
    }

    /// <summary>重置接收解码器（切换编码 / 重开端口时调用）。</summary>
    private void ResetRxDecoder(Encoding encoding)
    {
        lock (_rxDecoderLock)
        {
            _rxDecoder = encoding?.GetDecoder() ?? Encoding.ASCII.GetDecoder();
        }
    }

    /// <summary>如当前解码器对应编码不对则重建。</summary>
    private void EnsureRxDecoder(Encoding encoding)
    {
        lock (_rxDecoderLock)
        {
            if (_rxDecoder == null ||
                !string.Equals(_rxDecoder.GetType().FullName, encoding?.GetDecoder()?.GetType()?.FullName,
                    StringComparison.Ordinal))
            {
                // 简化判定：直接用 WebName 比对。Decoder 类型本身与 Encoding 一一对应，
                // 无法直接取到 decoder 的 WebName，所以通过重走 Reset 即可。
                _rxDecoder = encoding?.GetDecoder() ?? Encoding.ASCII.GetDecoder();
            }
        }
    }

    /// <summary>
    /// ASCII 接收路径：通过 <see cref="Decoder.GetChars(char[], bool)"/> 增量解码，
    /// Decoder 内部会保留上次末字节（多字节字符的半截）并在下一次补充完整。
    /// </summary>
    private string DecodeBytesForDisplay(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;
        Decoder decoder;
        lock (_rxDecoderLock)
        {
            decoder = _rxDecoder ?? Encoding.ASCII.GetDecoder();
        }

        int charCount = decoder.GetCharCount(bytes, 0, bytes.Length, flush: false);
        if (charCount <= 0) return string.Empty;
        var chars = new char[charCount];
        decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
        return EscapeForDisplay(new string(chars));
    }

    /// <summary>将已解码字符串中的控制字符转义显示（保持可打印）。</summary>
    private static string EscapeForDisplay(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool TryParseHex(string input, out byte[] result)
    {
        result = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        var tokens = input.Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new System.Collections.Generic.List<byte>(tokens.Length);
        foreach (var t in tokens)
        {
            if (t.Length is 0 or > 2)
            {
                return false;
            }
            if (!byte.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return false;
            }
            list.Add(b);
        }
        result = list.ToArray();
        return result.Length > 0;
    }

    private static Encoding ResolveEncoding(string webName)
    {
        // .NET Core/.NET 5+ 中 GB2312 等需注册 Provider
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(webName);
        }
        catch
        {
            return Encoding.ASCII;
        }
    }

    #endregion
}

/// <summary>
/// UI 绑定的编码下拉项。
/// </summary>
public class EncodingOption
{
    public string Display { get; }
    public string Value { get; }

    public EncodingOption(string display, string value)
    {
        Display = display;
        Value = value;
    }

    public override string ToString() => Display;
}
