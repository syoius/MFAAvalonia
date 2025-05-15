using Avalonia.Controls.ApplicationLifetimes;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Utilities.Attributes;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.UserControls.Settings;
using MFAAvalonia.Views.Windows;
using System;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MFAAvalonia.Helper;

#pragma warning disable CS0169 // The field is never used
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor

[LazyStatic]
public static partial class Instances
{
    #region Core Resolver

    private static readonly ConcurrentDictionary<Type, Lazy<object>> ServiceCache = new();

    /// <summary>
    /// 解析服务（自动缓存 + 循环依赖检测）
    /// </summary>
    private static T Resolve<T>()
    {
        var serviceType = typeof(T);
        var lazy = ServiceCache.GetOrAdd(serviceType, _ =>
            new Lazy<object>(
                () =>
                {
                    try { return App.Services.GetRequiredService<T>(); }
                    // try { return new T(); }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to resolve service {typeof(T).Name}. Possible causes: 1. Service not registered; 2. Circular dependency detected; 3. Thread contention during initialization.", ex);
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication
            ));
        return (T)lazy.Value;
    }

    #endregion

    /// <summary>
    /// 关闭当前应用程序
    /// </summary>
    public static void ShutdownApplication()
    {
        DispatcherHelper.RunOnMainThread(() => ApplicationLifetime.MainWindow?.Close());
    }

    /// <summary>
    /// 重启当前应用程序
    /// </summary>
    public static void RestartApplication(bool noAutoStart = false)
    {
        if (noAutoStart)
            GlobalConfiguration.SetValue(ConfigurationKeys.NoAutoStart, bool.TrueString);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(),
                UseShellExecute = true
            }
        };

        try
        {
            process.Start();
            ShutdownApplication();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"重启失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 关闭操作系统（需要管理员权限）
    /// </summary>
    public static void ShutdownSystem()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("shutdown", "/s /t 0");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("shutdown", "-h now");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("sudo", "shutdown -h now");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"关机失败: {ex.Message}");
        }
    }
    /// <summary>
    /// 跨平台重启操作系统（需要管理员/root权限）
    /// </summary>
    public static void RestartSystem()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows重启命令[8,3](@ref)
                using var process = new Process();
                process.StartInfo.FileName = "shutdown.exe";
                process.StartInfo.Arguments = "/r /t 0 /f"; // /f 强制关闭所有程序
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.Verb = "runas"; // 请求管理员权限
                process.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux重启命令[7,3](@ref)
                using var process = new Process();
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = "-c \"sudo shutdown -r now\"";
                process.StartInfo.RedirectStandardInput = true;
                process.Start();
                process.StandardInput.WriteLine("password"); // 需替换实际密码或配置免密sudo
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS重启命令[3,7](@ref)
                using var process = new Process();
                process.StartInfo.FileName = "/usr/bin/sudo";
                process.StartInfo.Arguments = "shutdown -r now";
                process.StartInfo.UseShellExecute = true;
                process.Start();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"重启失败: {ex.Message}");
            // 备用方案：尝试通用POSIX命令
            TryFallbackReboot();
        }
    }

    /// <summary>
    /// 备用重启方案（兼容非标准环境）
    /// </summary>
    private static void TryFallbackReboot()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = MFAExtensions.GetFallbackCommand(),
                UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                CreateNoWindow = true
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.Arguments = "/c shutdown /r /t 0";
            }
            else
            {
                psi.Arguments = "-c \"sudo reboot\"";
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"备用重启方案失败: {ex.Message}");
        }
    }


    private static string GetExecutablePath()
    {
        // 兼容.NET 5+环境
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory;
    }

    private static IClassicDesktopStyleApplicationLifetime _applicationLifetime;
    private static ISukiToastManager _toastManager;
    private static ISukiDialogManager _dialogManager;

    private static RootView _rootView;
    private static RootViewModel _rootViewModel;

    private static TaskQueueView _taskQueueView;
    private static TaskQueueViewModel _taskQueueViewModel;
    private static SettingsView _settingsView;
    private static SettingsViewModel _settingsViewModel;
    private static ResourcesView _resourcesView;
    private static ResourcesViewModel _resourcesViewModel;

    private static AnnouncementViewModel _announcementViewModel;

    private static ConnectSettingsUserControl _connectSettingsUserControl;
    private static ConnectSettingsUserControlModel _connectSettingsUserControlModel;
    private static GuiSettingsUserControl _guiSettingsUser;
    private static GuiSettingsUserControlModel _guiSettingsUserControlModel;
    private static ConfigurationMgrUserControl _configurationMgrUserControl;
    private static ExternalNotificationSettingsUserControl _externalNotificationSettingsUserControl;
    private static ExternalNotificationSettingsUserControlModel _externalNotificationSettingsUserControlModel;
    private static TimerSettingsUserControl _timerSettingsUserControl;
    private static TimerSettingsUserControlModel _timerSettingsUserControlModel;
    private static PerformanceUserControl _performanceUserControl;
    private static PerformanceUserControlModel _performanceUserControlModel;
    private static GameSettingsUserControl _gameSettingsUserControl;
    private static GameSettingsUserControlModel _gameSettingsUserControlModel;
    private static VersionUpdateSettingsUserControl _versionUpdateSettingsUserControl;
    private static VersionUpdateSettingsUserControlModel _versionUpdateSettingsUserControlModel;
    private static StartSettingsUserControl _startSettingsUserControl;
    private static StartSettingsUserControlModel _startSettingsUserControlModel;
    private static AboutUserControl _aboutUserControl;
    private static HotKeySettingsUserControl _hotKeySettingsUserControl;
}
