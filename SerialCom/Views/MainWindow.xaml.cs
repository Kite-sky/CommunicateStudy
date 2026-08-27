using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using SerialCom.ViewModels;

namespace SerialCom.Views;

/// <summary>
/// MainWindow 代码后置：仅负责日志自动滚动等纯视图行为。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Log.CollectionChanged += OnLogChanged;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 窗口关闭前让 VM 清理串口资源（IDisposable.Dispose / Shutdown）
        if (DataContext is MainWindowViewModel v)
        {
            if (v is IDisposable d)
                d.Dispose();
            else
                v.Shutdown();
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.AutoScrollLog && e.Action == NotifyCollectionChangedAction.Add)
        {
            // 在下一帧滚动，确保新项已完成布局
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (LogList.Items.Count > 0)
                {
                    LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                }
            }));
        }
    }

    private void CheckBox_Checked(object sender, RoutedEventArgs e)
    {

    }
}
