using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CommunicateDemo.Services;

/// <summary>
/// 简单日志文件记录器 —— 追加写，按日期命名
/// </summary>
public static class FileLogger
{
    private static readonly object _lock = new();

    /// <summary>日志目录（程序运行目录下的 Logs）</summary>
    public static string LogDir { get; } = Path.Combine(AppContext.BaseDirectory, "Logs");

    static FileLogger()
    {
        if (!Directory.Exists(LogDir))
        {
            try { Directory.CreateDirectory(LogDir); } catch { }
        }
    }

    /// <summary>追加一行日志</summary>
    public static void Write(string category, string message)
    {
        try
        {
            lock (_lock)
            {
                var path = Path.Combine(LogDir, $"log-{DateTime.Now:yyyyMMdd}.txt");
                using var sw = File.AppendText(path);
                sw.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}");
            }
        }
        catch { /* 写日志失败不影响主流程 */ }
    }

    /// <summary>追加多行原始内容（用于导出）</summary>
    public static void WriteRaw(string path, string content)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(path, content);
            }
        }
        catch { }
    }
}
