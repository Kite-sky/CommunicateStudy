﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;

namespace CommunicateDemo.AsyncAndAwait;

internal class DownloadString
{
    Stopwatch sw = new Stopwatch();
    const int MaxCount = 6000000;

    /// <summary>
    /// 日志消息事件，用于把原本输出到调试窗口的内容转发到页面。
    /// </summary>
    public event Action<string>? LogMessage;

    private void WriteLog(string message)
    {
        Debug.WriteLine(message);
        LogMessage?.Invoke(message);
    }

    public void DoRun()
    {
        
        sw.Start();
        Task<long> t1 = CountCharactersAsync(1, "https://www.baidu.com/");
        Task<long> t2 = CountCharactersAsync(2, "https://cn.bing.com/");
        CountToALargeNumber(1);
        CountToALargeNumber(2);
        CountToALargeNumber(3);
        CountToALargeNumber(4);
        WriteLog($"Char in https://www.baidu.com : {t1.Result}");
        WriteLog($"Char in https://cn.bing.com/ : {t2.Result}");
    }

    private async Task<long> CountCharactersAsync(int v1, string v2)
    {
        HttpClient wc = new HttpClient();
        WriteLog($"Start Call {v1} : {sw.Elapsed.TotalMilliseconds}ms");
        var result = await wc.GetStringAsync(new Uri(v2)).ConfigureAwait(false);
        WriteLog($"Call {v1} completed: {sw.Elapsed.TotalMilliseconds}ms");
        return result.Length;
    }

    private void CountToALargeNumber(int v1)
    {
        for (int i = 0; i < MaxCount; i++) ;
        WriteLog($"End Counting {v1}: {sw.Elapsed.TotalMilliseconds}ms");
    }
}
