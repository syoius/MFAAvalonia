using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Views.UserControls;
using Newtonsoft.Json;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MFAAvalonia.Views.Windows;

public partial class RootView : SukiWindow
{
    public RootView()
    {
        // 添加初始化标志
        _isInitializing = true;

        // 先从配置中加载窗口大小，在窗口显示前设置
        try
        {
            var widthStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowWidth, "");
            var heightStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowHeight, "");

            if (!string.IsNullOrEmpty(widthStr) && !string.IsNullOrEmpty(heightStr))
            {
                if (double.TryParse(widthStr, out double width) && double.TryParse(heightStr, out double height))
                {
                    if (width > 100 && height > 100) // 确保有效的窗口大小
                    {
                        // 直接设置窗口初始大小
                        Width = width;
                        Height = height;
                        LoggerHelper.Info($"窗口初始大小设置为: 宽度={width}, 高度={height}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载初始窗口大小失败: {ex.Message}");
        }

        // 初始化组件
        InitializeComponent();

        // 设置事件处理
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                HandleWindowStateChange();
            }
        };

        // 为窗口大小变化添加监听，保存窗口大小
        SizeChanged += SaveWindowSizeOnChange;

        // 修改Loaded事件处理
        Loaded += (_, _) =>
        {
            LoggerHelper.Info("窗口Loaded事件触发");

            // 确保在UI线程上执行
            Dispatcher.UIThread.Post(() =>
            {
                // 初始化完成
                _isInitializing = false;

                // 加载UI
                LoadUI();
            });
        };

        MaaProcessor.Instance.InitializeData();
    }

    private bool _isInitializing = true;

    private void HandleWindowStateChange()
    {
        if (ConfigurationManager.Current.GetValue(ConfigurationKeys.ShouldMinimizeToTray, false))
        {
            Instances.RootViewModel.IsWindowVisible = WindowState != WindowState.Minimized;
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
#pragma warning disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。请考虑将 "await" 运算符应用于调用结果。
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Instances.RootViewModel.IsRunning)
        {
            e.Cancel = true;
            ConfirmExit(() => OnClosed(e));
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, Instances.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));

        // 确保窗口大小被保存
        SaveWindowSize();

        MaaProcessor.Instance.SetTasker();
        GlobalHotkeyService.Shutdown();
        base.OnClosed(e);
    }

    public async Task<bool> ConfirmExit(Action? action = null)
    {
        if (!Instances.RootViewModel.IsRunning)
            return true;

        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = "ConfirmExitText".ToLocalization(),
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions()
        {
            Title = "ConfirmExitTitle".ToLocalization(),
        });

        if (result is SukiMessageBoxResult.Yes)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                LoggerHelper.Error(e);
            }
            finally { Environment.Exit(0); }

            return true;
        }
        return false;
    }

    public static void AddLogByColor(string content,
        string brush = "Gray",
        string weight = "Regular",
        bool showTime = true)
        =>
            Instances.TaskQueueViewModel.AddLog(content, brush, weight, showTime);


    public static void AddLog(string content,
        IBrush? brush = null,
        string weight = "Regular",
        bool showTime = true)
        =>
            Instances.TaskQueueViewModel.AddLog(content, brush, weight, showTime);

    public static void AddLogByKey(string key, IBrush? brush = null, bool transformKey = true, params string[] formatArgsKeys)
        => Instances.TaskQueueViewModel.AddLogByKey(key, brush, transformKey, formatArgsKeys);

#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。请考虑将 "await" 运算符应用于调用结果。
    public void LoadUI()
    {

        DispatcherHelper.RunOnMainThread(async () =>
        {
            await Task.Delay(300);
            Instances.TaskQueueViewModel.CurrentController = (MaaProcessor.Interface?.Controller?.FirstOrDefault()?.Type).ToMaaControllerTypes(Instances.TaskQueueViewModel.CurrentController);
            if (!Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.NoAutoStart, bool.FalseString))
                && ConfigurationManager.Current.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("Startup", StringComparison.OrdinalIgnoreCase))
            {
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "启动前",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await MaaProcessor.Instance.WaitSoftware(),
                });
                MaaProcessor.Instance.Start(!ConfigurationManager.Current.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("And", StringComparison.OrdinalIgnoreCase), checkUpdate: true);
            }
            else
            {
                var isAdb = Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb;

                AddLogByKey("ConnectingTo", null, true, isAdb ? "Emulator" : "Window");

                Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig();
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "连接检测",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await MaaProcessor.Instance.TestConnecting(),
                });
                MaaProcessor.Instance.Start(true, checkUpdate: true);
            }
            try
            {
                var tempMFADir = Path.Combine(AppContext.BaseDirectory, "temp_mfa");
                if (Directory.Exists(tempMFADir))
                    Directory.Delete(tempMFADir, true);

                var tempMaaDir = Path.Combine(AppContext.BaseDirectory, "temp_maafw");
                if (Directory.Exists(tempMaaDir))
                    Directory.Delete(tempMaaDir, true);

                var tempResDir = Path.Combine(AppContext.BaseDirectory, "temp_res");
                if (Directory.Exists(tempResDir))
                    Directory.Delete(tempResDir, true);
            }
            catch (Exception e)
            {
                LoggerHelper.Error(e);
            }
            GlobalConfiguration.SetValue(ConfigurationKeys.NoAutoStart, bool.FalseString);

            Instances.RootViewModel.LockController = (MaaProcessor.Interface?.Controller?.Count ?? 0) < 2;
            ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableEdit, ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableEdit, false));

            foreach (var task in Instances.TaskQueueViewModel.TaskItemViewModels)
            {
                if (task.InterfaceItem?.Advanced is { Count: > 0 } || task.InterfaceItem?.Option is { Count: > 0 } || task.InterfaceItem?.Document != null || task.InterfaceItem?.Repeatable == true)
                {
                    task.EnableSetting = true;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(MaaProcessor.Interface?.Message))
            {
                ToastHelper.Info(MaaProcessor.Interface.Message);
            }

        });

        TaskManager.RunTaskAsync(async () =>
        {
            await Task.Delay(1000);
            DispatcherHelper.RunOnMainThread(() =>
            {
                Instances.AnnouncementViewModel.CheckAnnouncement();
                if (ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoMinimize, false))
                {
                    WindowState = WindowState.Minimized;
                }
                if (ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoHide, false))
                {
                    Hide();
                }
            });
        });
    }

    public void ClearTasks(Action? action = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            Instances.TaskQueueViewModel.TaskItemViewModels = new();
            action?.Invoke();
        });
    }

    private void RestoreWindowSize()
    {
        // 此方法保留作为备用，但不再在窗口加载后调用
        try
        {
            var configName = ConfigurationManager.Current.FileName;
            var widthStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowWidth, "");
            var heightStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowHeight, "");

            LoggerHelper.Info($"尝试恢复窗口大小: 宽度={widthStr}, 高度={heightStr}, 配置={configName}");

            if (!string.IsNullOrEmpty(widthStr) && !string.IsNullOrEmpty(heightStr))
            {
                if (double.TryParse(widthStr, out double width) && double.TryParse(heightStr, out double height))
                {
                    if (width > 100 && height > 100) // 确保有效的窗口大小
                    {
                        // 临时解除事件绑定，避免触发保存
                        SizeChanged -= SaveWindowSizeOnChange;

                        // 直接设置窗口大小，确保在UI线程上执行
                        Width = width;
                        Height = height;
                        LoggerHelper.Info($"窗口大小恢复成功: 宽度={width}, 高度={height}");

                        // 重新绑定事件
                        SizeChanged += SaveWindowSizeOnChange;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"恢复窗口大小失败: {ex.Message}");
        }
    }

    private void SaveWindowSizeOnChange(object? sender, SizeChangedEventArgs e)
    {
        // 初始化过程中不保存窗口大小
        if (!_isInitializing)
        {
            SaveWindowSize();
        }
    }

    private void SaveWindowSize()
    {
        // 初始化过程中不保存窗口大小
        if (_isInitializing)
        {
            return;
        }

        try
        {
            // 获取当前窗口大小
            double width = Width;
            double height = Height;
            if (width > 100 && height > 100) // 确保有效的窗口大小
            {
                // 检查是否与当前配置值不同，避免不必要的保存
                string currentWidthValue = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowWidth, "0");
                string currentHeightValue = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowHeight, "0");

                bool widthChanged = !string.Equals(width.ToString(), currentWidthValue);
                bool heightChanged = !string.Equals(height.ToString(), currentHeightValue);

                if (widthChanged || heightChanged)
                {
                    // 保存窗口大小到配置并立即写入文件
                    ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowWidth, width.ToString());
                    ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowHeight, height.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存窗口大小失败: {ex.Message}");
        }
    }
}
