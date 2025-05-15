using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.UserControls;
using MFAAvalonia.Views.UserControls.Settings;
using MFAAvalonia.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia;

public partial class App : Application
{
    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LanguageHelper.Initialize();
        ConfigurationManager.Initialize();
        var cracker = new AvaloniaMemoryCracker();
        cracker.Cracker();
        GlobalHotkeyService.Initialize();
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException; //Task线程内未捕获异常处理事件
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException; //非UI线程内未捕获异常处理事件
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException; //UI线程内未捕获异常处理事件

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();

            services.AddSingleton(desktop);

            ConfigureServices(services);

            var views = ConfigureViews(services);

            Services = services.BuildServiceProvider();

            DataTemplates.Add(new ViewLocator(views));

            var window = views.CreateView<RootViewModel>(Services) as Window;

            desktop.MainWindow = window;

            TrayIconManager.InitializeTrayIcon(this, Instances.RootView, Instances.RootViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ViewsHelper ConfigureViews(ServiceCollection services)
    {

        return new ViewsHelper()

            // Add main view
            .AddView<RootView, RootViewModel>(services)

            // Add pages
            .AddView<TaskQueueView, TaskQueueViewModel>(services)
            .AddView<ResourcesView, ResourcesViewModel>(services)
            .AddView<SettingsView, SettingsViewModel>(services)

            // Add additional views
            .AddView<AddTaskDialogView, AddTaskDialogViewModel>(services)
            .AddView<AdbEditorDialogView, AdbEditorDialogViewModel>(services)
            .AddView<CustomThemeDialogView, CustomThemeDialogViewModel>(services)
            
            .AddView<ConnectSettingsUserControl, ConnectSettingsUserControlModel>(services)
            .AddView<GameSettingsUserControl, GameSettingsUserControlModel>(services)
            .AddView<GuiSettingsUserControl, GuiSettingsUserControlModel>(services)
            .AddView<StartSettingsUserControl, StartSettingsUserControlModel>(services)
            .AddView<ExternalNotificationSettingsUserControl, ExternalNotificationSettingsUserControlModel>(services)
            .AddView<TimerSettingsUserControl, TimerSettingsUserControlModel>(services)
            .AddView<PerformanceUserControl, PerformanceUserControlModel>(services)
            .AddView<VersionUpdateSettingsUserControl, VersionUpdateSettingsUserControlModel>(services)
            
            .AddOnlyViewModel<AnnouncementView, AnnouncementViewModel>(services)
            .AddOnlyView<AboutUserControl, SettingsViewModel>(services)
            .AddOnlyView<HotKeySettingsUserControl, SettingsViewModel>(services)
            .AddOnlyView<ConfigurationMgrUserControl, SettingsViewModel>(services);
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<ISukiToastManager, SukiToastManager>();
        services.AddSingleton<ISukiDialogManager, SukiDialogManager>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            e.Handled = true;
            LoggerHelper.Error(e.Exception);
            ErrorView.ShowException(e.Exception);
        }
        catch (Exception ex)
        {
            //此时程序出现严重异常，将强制结束退出
            LoggerHelper.Error(ex.ToString());
            ErrorView.ShowException(ex, true);
        }
    }


    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var sbEx = new StringBuilder();
        if (e.IsTerminating)
            sbEx.Append("非UI线程发生致命错误");
        else
            sbEx.Append("非UI线程异常：");
        if (e.ExceptionObject is Exception ex)
        {
            ErrorView.ShowException(ex);
            sbEx.Append(ex.Message);
        }
        else
        {
            sbEx.Append(e.ExceptionObject);
        }
        LoggerHelper.Error(sbEx);
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        //task线程内未处理捕获
        LoggerHelper.Error(e.Exception);
        ErrorView.ShowException(e.Exception);
        foreach (var item in e.Exception.InnerExceptions)
        {
            LoggerHelper.Error(string.Format("异常类型：{0}{1}来自：{2}{3}异常内容：{4}",
                item.GetType(), Environment.NewLine, item.Source,
                Environment.NewLine, item.Message));
        }

        e.SetObserved(); //设置该异常已察觉（这样处理后就不会引起程序崩溃）
    }
    
    
}
