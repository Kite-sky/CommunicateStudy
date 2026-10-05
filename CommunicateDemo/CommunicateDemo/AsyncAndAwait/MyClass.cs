using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CommunicateDemo.AsyncAndAwait;

internal class MyClass
{
    public event Action<string>? LogMessage;

    private void WriteLog(string message)
    {
        Debug.WriteLine(message);
        LogMessage?.Invoke(message);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await CycleMethodAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            WriteLog("Cycle method cancelled.");
        }
    }

    private async Task CycleMethodAsync(CancellationToken ct)
    {
        WriteLog("Cycle method executed.");
        const int MaxCount = 5;
        for (int i = 0; i < MaxCount; i++)
        {
            ct.ThrowIfCancellationRequested();                        // 每次循环开头检查
            await Task.Delay(1000, ct).ConfigureAwait(false);          // 等待可被取消，立即中断
            WriteLog($"Cycle method iteration {i + 1} completed.");
        }
    }
}
