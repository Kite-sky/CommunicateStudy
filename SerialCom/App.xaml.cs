using System.Windows;
using Prism.DryIoc;
using Prism.Ioc;
using SerialCom.Services;
using SerialCom.ViewModels;
using SerialCom.Views;

namespace SerialCom;

/// <summary>
/// Prism 应用入口，使用 DryIoc 容器。
/// </summary>
public partial class App : PrismApplication
{
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // --- 服务 ---
        // 串口服务工厂：按通信模式（RS232 / RS485）创建实例
        containerRegistry.RegisterSingleton<ISerialServiceFactory, SerialServiceFactory>();
        // 设置持久化
        containerRegistry.RegisterSingleton<ISettingsService, SettingsService>();

        // --- 视图 - VM 显式绑定（可追踪，不依赖 ViewModelLocator 命名约定） ---
        containerRegistry.RegisterForNavigation<MainWindow, MainWindowViewModel>();
    }
}
