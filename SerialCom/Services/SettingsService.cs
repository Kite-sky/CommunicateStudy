using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SerialCom.Models;

namespace SerialCom.Services;

/// <summary>
/// 应用级设置，可序列化到 JSON 文件。
/// </summary>
public class AppSettings
{
    // --- 串口配置 ---
    public CommunicationMode Mode { get; set; } = CommunicationMode.Rs232;
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Parity Parity { get; set; } = Parity.None;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StopBits StopBits { get; set; } = StopBits.One;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Handshake Handshake { get; set; } = Handshake.None;

    public bool RtsHighWhenTransmitting { get; set; } = true;

    // --- 显示/发送格式 ---
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DataFormat DataFormat { get; set; } = DataFormat.Ascii;

    public string TextEncodingName { get; set; } = "us-ascii";
    public bool AppendNewLineOnSend { get; set; } = true;
    public bool AutoScrollLog { get; set; } = true;

    // --- 周期发送 ---
    public bool AutoSendEnabled { get; set; } = false;
    public int AutoSendIntervalMs { get; set; } = 1000;
    public string AutoSendText { get; set; } = string.Empty;

    // --- UI ---
    public double LogMaxLines { get; set; } = 10000;
}

/// <summary>
/// 负责加载/保存 AppSettings 到本地文件（用户 AppData 目录）。
/// </summary>
public interface ISettingsService
{
    /// <summary>从磁盘加载，找不到或出错则返回默认值。</summary>
    AppSettings Load();

    /// <summary>保存到磁盘。</summary>
    void Save(AppSettings settings);
}

public class SettingsService : ISettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SerialCom");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch
        {
            // 解析失败 -> 使用默认值
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
            }
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json, Encoding.UTF8);
        }
        catch
        {
            // 保存失败静默，至少不让程序崩
        }
    }
}
