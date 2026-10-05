using CommunicateDemo.AsyncAndAwait;
using System.Windows;

namespace CommunicateDemo
{
    /// <summary>
    /// AsyncAndAwaitWindow.xaml 的交互逻辑
    /// </summary>
    public partial class AsyncAndAwaitWindow : Window
    {
        public AsyncAndAwaitWindow()
        {
            InitializeComponent();
            Test();
        }

        private async void Test()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            MyClass mc = new MyClass();
            mc.LogMessage += AppendLog;
            Task t = mc.RunAsync(token);

            await Task.Delay(3000);   // 不阻塞 UI 线程，日志队列能实时按序处理
            cts.Cancel();
            await t;

            // 走同一条 AppendLog（BeginInvoke 排队）路径，保证排在所有日志之后显示
            AppendLog($"Was Cancelled {token.IsCancellationRequested}");
        }

        private void AppendLog(string message)
        {
            // 异步回调可能来自线程池线程，需切回 UI 线程再更新控件
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();
            }));
        }
    }
}
