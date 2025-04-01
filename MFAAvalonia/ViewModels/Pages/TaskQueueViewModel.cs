using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.Views.Windows;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Enums;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Layout;

namespace MFAAvalonia.ViewModels.Pages;

public partial class TaskQueueViewModel : ViewModelBase
{
    protected override void Initialize()
    {
        try
        {
            var col1Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn1Width, DefaultColumn1Width);
            var col2Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn2Width, DefaultColumn2Width);
            var col3Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn3Width, DefaultColumn3Width);
            
            SuppressPropertyChangedCallbacks = true;
            
            Column1Width = GridLength.Parse(col1Str);
            Column2Width = GridLength.Parse(col2Str);
            Column3Width = GridLength.Parse(col3Str);
            
            SuppressPropertyChangedCallbacks = false;
            
            LoggerHelper.Info("构造函数中列宽设置成功");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"构造函数中设置列宽失败: {ex.Message}");
            SetDefaultColumnWidths();
        }
    }
    
    #region 介绍

    [ObservableProperty] private string _introduction = string.Empty;

    // 确保Markdown内容能够正确处理换行
    public void SetMarkdownIntroduction(string markdown)
    {
        // 确保换行符被正确处理
        if (!string.IsNullOrEmpty(markdown))
        {
            // 替换Windows风格的换行符
            markdown = markdown.Replace("\r\n", "\n");
            
            // 确保段落间有空行
            markdown = Regex.Replace(markdown, @"(?<!\n)\n(?!\n)", "  \n");
            
            // 替换连续换行为段落分隔
            markdown = Regex.Replace(markdown, @"(\n\s*\n)", "\n\n");
        }
        
        Introduction = markdown;
    }

    #endregion

    #region 任务

    [ObservableProperty] private ObservableCollection<DragItemViewModel> _taskItemViewModels = [];

    partial void OnTaskItemViewModelsChanged(ObservableCollection<DragItemViewModel> value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, value.ToList().Select(model => model.InterfaceItem));
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Instances.RootViewModel.IsRunning)
            StopTask();
        else
            StartTask();
    }

    public void StartTask()
    {
        MaaProcessor.Instance.Start();
    }

    public void StopTask()
    {
        MaaProcessor.Instance.Stop();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var task in TaskItemViewModels)
            task.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var task in TaskItemViewModels)
            task.IsChecked = false;
    }

    [RelayCommand]
    private void AddTask()
    {
        Instances.DialogManager.CreateDialog().WithTitle("AdbEditor").WithViewModel(dialog => new AddTaskDialogViewModel(dialog, MaaProcessor.Instance.TasksSource)).TryShow();
    }

    #endregion

    #region 日志

    public ObservableCollection<LogItemViewModel> LogItemViewModels { get; } = new();

    public static string FormatFileSize(long size)
    {
        string unit;
        double value;
        if (size >= 1024L * 1024 * 1024 * 1024)
        {
            value = (double)size / (1024L * 1024 * 1024 * 1024);
            unit = "TB";
        }
        else if (size >= 1024 * 1024 * 1024)
        {
            value = (double)size / (1024 * 1024 * 1024);
            unit = "GB";
        }
        else if (size >= 1024 * 1024)
        {
            value = (double)size / (1024 * 1024);
            unit = "MB";
        }
        else if (size >= 1024)
        {
            value = (double)size / 1024;
            unit = "KB";
        }
        else
        {
            value = size;
            unit = "B";
        }

        return $"{value:F} {unit}";
    }

    public static string FormatDownloadSpeed(double speed)
    {
        string unit;
        double value = speed;
        if (value >= 1024L * 1024 * 1024 * 1024)
        {
            value /= 1024L * 1024 * 1024 * 1024;
            unit = "TB/s";
        }
        else if (value >= 1024L * 1024 * 1024)
        {
            value /= 1024L * 1024 * 1024;
            unit = "GB/s";
        }
        else if (value >= 1024 * 1024)
        {
            value /= 1024 * 1024;
            unit = "MB/s";
        }
        else if (value >= 1024)
        {
            value /= 1024;
            unit = "KB/s";
        }
        else
        {
            unit = "B/s";
        }

        return $"{value:F} {unit}";
    }
    public void OutputDownloadProgress(long value = 0, long maximum = 1, int len = 0, double ts = 1)
    {
        string sizeValueStr = FormatFileSize(value);
        string maxSizeValueStr = FormatFileSize(maximum);
        string speedValueStr = FormatDownloadSpeed(len / ts);

        string progressInfo = $"[{sizeValueStr}/{maxSizeValueStr}({100 * value / maximum}%) {speedValueStr}]";
        OutputDownloadProgress(progressInfo);
    }

    public void ClearDownloadProgress()
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            if (LogItemViewModels.Count > 0 && LogItemViewModels[0].IsDownloading)
            {
                LogItemViewModels.RemoveAt(0);
            }
        });
    }

    public void OutputDownloadProgress(string output, bool downloading = true)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            var log = new LogItemViewModel(downloading ? "NewVersionFoundDescDownloading".ToLocalization() + "\n" + output : output, Instances.RootView.FindResource("SukiAccentColor") as IBrush,
                dateFormat: "HH':'mm':'ss")
            {
                IsDownloading = true,
            };
            if (LogItemViewModels.Count > 0 && LogItemViewModels[0].IsDownloading)
            {
                if (!string.IsNullOrEmpty(output))
                {
                    LogItemViewModels[0] = log;
                }
                else
                {
                    LogItemViewModels.RemoveAt(0);
                }
            }
            else if (!string.IsNullOrEmpty(output))
            {
                LogItemViewModels.Insert(0, log);
            }
        });
    }

    public const string INFO = "info:";
    public const string ERROR = "err:";
    public const string WARN = "warn:";
    public void AddLog(string content,
        IBrush? brush,
        string weight = "Regular",
        bool showTime = true)
    {
        brush ??= Brushes.Black;
        var changeColor = true;
        if (content.StartsWith(INFO))
        {
            brush = Brushes.Black;
            content = content.Substring(INFO.Length);
        }
        if (content.StartsWith(WARN))
        {
            brush = Brushes.Orange;
            content = content.Substring(WARN.Length);
            changeColor = false;
        }
        if (content.StartsWith(ERROR))
        {
            brush = Brushes.OrangeRed;
            content = content.Substring(ERROR.Length);
            changeColor = false;
        }
        Task.Run(() =>
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                LogItemViewModels.Add(new LogItemViewModel(content, brush, weight, "HH':'mm':'ss",
                    showTime: showTime, changeColor: changeColor));
                LoggerHelper.Info(content);
            });
        });
    }

    public void AddLog(string content,
        string color = "",
        string weight = "Regular",
        bool showTime = true)
    {
        var brush = BrushHelper.ConvertToBrush(color, Brushes.Black);
        AddLog(content, brush, weight, showTime);
    }

    public void AddLogByKey(string key, IBrush? brush = null, bool transformKey = true, params string[] formatArgsKeys)
    {
        brush ??= Brushes.Black;
        Task.Run(() =>
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                var log = new LogItemViewModel(key, brush, "Regular", true, "HH':'mm':'ss", showTime: true, transformKey: transformKey, formatArgsKeys);
                LogItemViewModels.Add(log);
                LoggerHelper.Info(log.Content);
            });
        });
    }

    public void AddLogByKey(string key, string color = "", bool transformKey = true, params string[] formatArgsKeys)
    {
        var brush = BrushHelper.ConvertToBrush(color, Brushes.Black);
        AddLogByKey(key, brush, transformKey, formatArgsKeys);
    }

    #endregion

    #region 连接

    [ObservableProperty] private ObservableCollection<object> _devices = [];
    [ObservableProperty] private object? _currentDevice;
    private DateTime? _lastExecutionTime;
    partial void OnCurrentDeviceChanged(object? value)
    {

        if (value != null)
        {
            var now = DateTime.Now;
            if (_lastExecutionTime == null)
            {
                _lastExecutionTime = now;
            }
            else
            {
                if (now - _lastExecutionTime < TimeSpan.FromSeconds(1))
                    return;
                _lastExecutionTime = now;
            }
        }
        if (value is DesktopWindowInfo window)
        {
            ToastHelper.Info("WindowSelectionMessage".ToLocalizationFormatted(false, ""), window.Name);
            MaaProcessor.Config.DesktopWindow.Name = window.Name;
            MaaProcessor.Config.DesktopWindow.HWnd = window.Handle;
            MaaProcessor.Instance.SetTasker();
        }
        else if (value is AdbDeviceInfo device)
        {
            ToastHelper.Info("EmulatorSelectionMessage".ToLocalizationFormatted(false, ""), device.Name);
            MaaProcessor.Config.AdbDevice.Name = device.Name;
            MaaProcessor.Config.AdbDevice.AdbPath = device.AdbPath;
            MaaProcessor.Config.AdbDevice.AdbSerial = device.AdbSerial;
            MaaProcessor.Config.AdbDevice.Config = device.Config;
            MaaProcessor.Instance.SetTasker();
            ConfigurationManager.Current.SetValue(ConfigurationKeys.AdbDevice, device);
        }
    }

    [ObservableProperty] private MaaControllerTypes _currentController =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.CurrentController, MaaControllerTypes.Adb, MaaControllerTypes.None, new UniversalEnumConverter<MaaControllerTypes>());

    partial void OnCurrentControllerChanged(MaaControllerTypes value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.CurrentController, value.ToString());
        Refresh();
        MaaProcessor.Instance.SetTasker();
    }

    [ObservableProperty] private bool _isConnected;
    public void SetConnected(bool isConnected)
    {
        IsConnected = isConnected;
    }


    [RelayCommand]
    public void CustomAdb()
    {
        var deviceInfo = CurrentDevice as AdbDeviceInfo;

        Instances.DialogManager.CreateDialog().WithTitle("AdbEditor").WithViewModel(dialog => new AdbEditorDialogViewModel(deviceInfo, dialog)).Dismiss().ByClickingBackground().TryShow();
    }

    public static int ExtractNumberFromEmulatorConfig(string emulatorConfig)
    {
        var match = Regex.Match(emulatorConfig, @"\d+");

        if (match.Success)
        {
            return int.Parse(match.Value);
        }

        return 0;
    }

    public bool TryGetIndexFromConfig(string config, out int index)
    {
        try
        {
            using var doc = JsonDocument.Parse(config);
            if (doc.RootElement.TryGetProperty("extras", out var extras) && extras.TryGetProperty("mumu", out var mumu) && mumu.TryGetProperty("index", out var indexElement))
            {
                index = indexElement.GetInt32();
                return true;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error(ex);
        }

        index = 0;
        return false;
    }
    private CancellationTokenSource? _refreshCancellationTokenSource;
    [RelayCommand]
    private void Refresh()
    {
        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token), _refreshCancellationTokenSource.Token, handleError: (e) => HandleDetectionError(e, CurrentController == MaaControllerTypes.Adb),
            catchException: true, shouldLog: true);
    }
    
    [RelayCommand]
    private void Clear()
    {
        LogItemViewModels.Clear();
    }
    
    public void AutoDetectDevice(CancellationToken token = default)
    {
        var isAdb = CurrentController == MaaControllerTypes.Adb;

        ToastHelper.Info(GetDetectionMessage(isAdb));
        SetConnected(false);
        token.ThrowIfCancellationRequested();
        var (devices, index) = isAdb ? DetectAdbDevices() : DetectWin32Windows();
        token.ThrowIfCancellationRequested();
        UpdateDeviceList(devices, index);
        token.ThrowIfCancellationRequested();
        HandleControllerSettings(isAdb);
        token.ThrowIfCancellationRequested();
        UpdateConnectionStatus(devices.Count > 0, isAdb);

    }

    private string GetDetectionMessage(bool isAdb) =>
        (isAdb ? "EmulatorDetectionStarted" : "WindowDetectionStarted").ToLocalization();

    private (ObservableCollection<object> devices, int index) DetectAdbDevices()
    {
        var devices = MaaProcessor.Toolkit.AdbDevice.Find();
        var index = CalculateAdbDeviceIndex(devices);
        return (new(devices), index);
    }

    private int CalculateAdbDeviceIndex(IList<AdbDeviceInfo> devices)
    {
        var config = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
        if (string.IsNullOrWhiteSpace(config)) return 0;

        var targetNumber = ExtractNumberFromEmulatorConfig(config);
        return devices.Select((d, i) =>
                TryGetIndexFromConfig(d.Config, out var index) && index == targetNumber ? i : -1)
            .FirstOrDefault(i => i >= 0);
    }

    private (ObservableCollection<object> devices, int index) DetectWin32Windows()
    {
        Thread.Sleep(500);
        var windows = MaaProcessor.Toolkit.Desktop.Window.Find().Where(win => !string.IsNullOrWhiteSpace(win.Name)).ToList();
        var index = CalculateWindowIndex(windows);
        return (new(windows), index);
    }

    private int CalculateWindowIndex(List<DesktopWindowInfo> windows)
    {
        var controller = MaaProcessor.Interface?.Controller?
            .FirstOrDefault(c => c.Type?.Equals("win32", StringComparison.OrdinalIgnoreCase) == true);

        if (controller?.Win32 == null)
            return windows.FindIndex(win => !string.IsNullOrWhiteSpace(win.Name));

        var filtered = windows.Where(win =>
            !string.IsNullOrWhiteSpace(win.Name)).ToList();

        filtered = ApplyRegexFilters(filtered, controller.Win32);
        return filtered.Count > 0 ? windows.IndexOf(filtered.First()) : 0;
    }

    private List<DesktopWindowInfo> ApplyRegexFilters(List<DesktopWindowInfo> windows, MaaInterface.MaaResourceControllerWin32 win32)
    {
        var filtered = windows;
        if (!string.IsNullOrWhiteSpace(win32.WindowRegex))
        {
            var regex = new Regex(win32.WindowRegex);
            filtered = filtered.Where(w => regex.IsMatch(w.Name)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(win32.ClassRegex))
        {
            var regex = new Regex(win32.ClassRegex);
            filtered = filtered.Where(w => regex.IsMatch(w.ClassName)).ToList();
        }
        return filtered;
    }

    private void UpdateDeviceList(ObservableCollection<object> devices, int index)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            Devices = devices;
            if (devices.Count > index)
                CurrentDevice = devices[index];
        });
    }

    private void HandleControllerSettings(bool isAdb)
    {
        var controller = MaaProcessor.Interface?.Controller?
            .FirstOrDefault(c => c.Type?.Equals(isAdb ? "adb" : "win32", StringComparison.OrdinalIgnoreCase) == true);

        if (controller == null) return;

        HandleInputSettings(controller, isAdb);
        HandleScreenCapSettings(controller, isAdb);
    }

    private void HandleInputSettings(MaaInterface.MaaResourceController controller, bool isAdb)
    {
        var input = isAdb ? controller.Adb?.Input : controller.Win32?.Input;
        if (input == null) return;

        if (isAdb)
        {
            Instances.ConnectSettingsUserControlModel.AdbControlInputType = input switch
            {
                1 => AdbInputMethods.AdbShell,
                2 => AdbInputMethods.MinitouchAndAdbKey,
                4 => AdbInputMethods.Maatouch,
                8 => AdbInputMethods.EmulatorExtras,
                _ => Instances.ConnectSettingsUserControlModel.AdbControlInputType
            };
        }
        else
        {
            Instances.ConnectSettingsUserControlModel.Win32ControlInputType = input switch
            {
                1 => Win32InputMethod.Seize,
                2 => Win32InputMethod.SendMessage,
                _ => Instances.ConnectSettingsUserControlModel.Win32ControlInputType
            };
        }
    }

    private void HandleScreenCapSettings(MaaInterface.MaaResourceController controller, bool isAdb)
    {
        var screenCap = isAdb ? controller.Adb?.ScreenCap : controller.Win32?.ScreenCap;
        if (screenCap == null) return;
        if (isAdb)
        {
            Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType = screenCap switch
            {
                1 => AdbScreencapMethods.EncodeToFileAndPull,
                2 => AdbScreencapMethods.Encode,
                4 => AdbScreencapMethods.RawWithGzip,
                8 => AdbScreencapMethods.RawByNetcat,
                16 => AdbScreencapMethods.MinicapDirect,
                32 => AdbScreencapMethods.MinicapStream,
                64 => AdbScreencapMethods.EmulatorExtras,
                _ => Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType
            };
        }
        else
        {
            Instances.ConnectSettingsUserControlModel.Win32ControlScreenCapType = screenCap switch
            {
                1 => Win32ScreencapMethod.GDI,
                2 => Win32ScreencapMethod.FramePool,
                4 => Win32ScreencapMethod.DXGIDesktopDup,
                _ => Instances.ConnectSettingsUserControlModel.Win32ControlScreenCapType
            };
        }
    }

    private void UpdateConnectionStatus(bool hasDevices, bool isAdb)
    {
        if (!hasDevices)
        {
            ToastHelper.Info((
                isAdb ? "NoEmulatorFound" : "NoWindowFound").ToLocalization());
        }
    }

    private void HandleDetectionError(Exception ex, bool isAdb)
    {
        var targetType = isAdb ? "Emulator" : "Window";
        ToastHelper.Warn(string.Format(
            "TaskStackError".ToLocalization(),
            targetType.ToLocalization(),
            ex.Message));

        LoggerHelper.Error(ex);
    }

    public void TryReadAdbDeviceFromConfig(bool InTask = true, bool refresh = false)
    {
        if (refresh
            || CurrentController != MaaControllerTypes.Adb
            || !ConfigurationManager.Current.GetValue(ConfigurationKeys.RememberAdb, true)
            || MaaProcessor.Config.AdbDevice.AdbPath != "adb"
            || !ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AdbDevice, out AdbDeviceInfo device,
                new UniversalEnumConverter<AdbInputMethods>(), new UniversalEnumConverter<AdbScreencapMethods>()))
        {
            _refreshCancellationTokenSource?.Cancel();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            if (InTask)
                TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token));
            else
                AutoDetectDevice(_refreshCancellationTokenSource.Token);
            return;
        }

        DispatcherHelper.PostOnMainThread(() =>
        {
            Devices = [device];
            CurrentDevice = device;
        });
    }

    #endregion

    #region 资源

    [ObservableProperty] private ObservableCollection<MaaInterface.MaaInterfaceResource> _currentResources = [];

    [ObservableProperty] private string _currentResource = ConfigurationManager.Current.GetValue(ConfigurationKeys.Resource, string.Empty);

    partial void OnCurrentResourceChanged(string value) => HandlePropertyChanged(ConfigurationKeys.Resource, value);

    #endregion

    // 三列宽度配置
    private const string DefaultColumn1Width = "300";
    private const string DefaultColumn2Width = "1*";
    private const string DefaultColumn3Width = "1*";

    // 使用属性，标记为可通知属性，确保UI能正确绑定和监听变化
    [ObservableProperty] private GridLength _column1Width;
    [ObservableProperty] private GridLength _column2Width;
    [ObservableProperty] private GridLength _column3Width;

    // 添加记录拖拽开始的状态
    private GridLength _dragStartCol1Width;
    private GridLength _dragStartCol2Width;
    private GridLength _dragStartCol3Width;
    
    [RelayCommand]
    public void GridSplitterDragStarted(string splitterName)
    {
        try
        {
            _dragStartCol1Width = Column1Width;
            _dragStartCol2Width = Column2Width;
            _dragStartCol3Width = Column3Width;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"记录拖拽开始状态失败: {ex.Message}");
        }
    }

    [RelayCommand]
    public void GridSplitterDragCompleted(string splitterName)
    {
        try
        {
            // 记录拖拽完成事件和时间戳，方便分析日志
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            // 获取当前列宽
            var col1 = Column1Width;
            var col2 = Column2Width;
            var col3 = Column3Width;
            
            // 检查是否有变化
            bool col1Changed = !AreGridLengthsEqual(_dragStartCol1Width, col1);
            bool col2Changed = !AreGridLengthsEqual(_dragStartCol2Width, col2);
            bool col3Changed = !AreGridLengthsEqual(_dragStartCol3Width, col3);
            bool changed = col1Changed || col2Changed || col3Changed;
            
            var oldCol1Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn1Width, DefaultColumn1Width);
            var oldCol2Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn2Width, DefaultColumn2Width);
            var oldCol3Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn3Width, DefaultColumn3Width);
            
            var newCol1Str = col1.ToString();
            var newCol2Str = col2.ToString();
            var newCol3Str = col3.ToString();
            
            // 始终设置新值
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn1Width, newCol1Str);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn2Width, newCol2Str);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn3Width, newCol3Str);
            
            // 始终保存配置
            ConfigurationManager.SaveConfiguration(ConfigurationManager.Current.FileName);
            
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽配置失败: {ex.Message}");
        }
    }
    
    // 添加辅助方法用于精确比较两个GridLength
    private bool AreGridLengthsEqual(GridLength a, GridLength b)
    {
        if (a.GridUnitType != b.GridUnitType)
            return false;
            
        if (a.GridUnitType == GridUnitType.Auto || b.GridUnitType == GridUnitType.Auto)
            return a.GridUnitType == b.GridUnitType;
            
        // 对于像素值，允许0.5像素的误差
        if (a.GridUnitType == GridUnitType.Pixel)
            return Math.Abs(a.Value - b.Value) < 0.5;
            
        // 对于Star值，允许0.01的误差
        if (a.GridUnitType == GridUnitType.Star)
            return Math.Abs(a.Value - b.Value) < 0.01;
            
        return a.Value == b.Value;
    }

    partial void OnColumn1WidthChanged(GridLength value)
    {
        if (SuppressPropertyChangedCallbacks) return;
        
        try
        {
            // 获取旧值
            var oldValue = ConfigurationManager.Current.GetValue<string>(ConfigurationKeys.TaskQueueColumn1Width, DefaultColumn1Width);
            var newValue = value.ToString();
            
            // 使用改进的比较方法
            if (CompareGridLength(oldValue, value))
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn1Width, newValue);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽1失败: {ex.Message}");
        }
    }
    
    partial void OnColumn2WidthChanged(GridLength value)
    {
        if (SuppressPropertyChangedCallbacks) return;
        
        try
        {
            // 获取旧值
            var oldValue = ConfigurationManager.Current.GetValue<string>(ConfigurationKeys.TaskQueueColumn2Width, DefaultColumn2Width);
            var newValue = value.ToString();
            
            // 使用改进的比较方法
            if (CompareGridLength(oldValue, value))
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn2Width, newValue);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽2失败: {ex.Message}");
        }
    }
    
    partial void OnColumn3WidthChanged(GridLength value)
    {
        if (SuppressPropertyChangedCallbacks) return;
        
        try
        {
            // 获取旧值
            var oldValue = ConfigurationManager.Current.GetValue<string>(ConfigurationKeys.TaskQueueColumn3Width, DefaultColumn3Width);
            var newValue = value.ToString();
            
            // 使用改进的比较方法
            if (CompareGridLength(oldValue, value))
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn3Width, newValue);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽3失败: {ex.Message}");
        }
    }
    
    public bool SuppressPropertyChangedCallbacks { get; set; }

    // 保存列宽配置到磁盘
    public void SaveColumnWidths()
    {
        if (SuppressPropertyChangedCallbacks) return;
        
        try
        {
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn1Width, Column1Width.ToString());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn2Width, Column2Width.ToString());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn3Width, Column3Width.ToString());
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽配置失败: {ex.Message}");
        }
    }

    private void SetDefaultColumnWidths()
    {
        // 设置默认值
        try
        {
            SuppressPropertyChangedCallbacks = true;
            
            Column1Width = GridLength.Parse(DefaultColumn1Width);
            Column2Width = GridLength.Parse(DefaultColumn2Width);
            Column3Width = GridLength.Parse(DefaultColumn3Width);
            
            // 恢复属性更改通知
            SuppressPropertyChangedCallbacks = false;
            
            // 默认值需要保存，但只有在第一次启动时(无配置文件)
            if (!ConfigurationManager.Current.Config.ContainsKey(ConfigurationKeys.TaskQueueColumn1Width))
            {
                SaveColumnWidths();
            }
            
            LoggerHelper.Info("默认列宽设置成功");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"设置默认列宽失败: {ex.Message}");
        }
    }

    // 添加辅助方法用于比较GridLength
    private bool CompareGridLength(string storedValue, GridLength newValue)
    {
        // 先检查字符串是否完全相同
        var newValueStr = newValue.ToString();
        if (string.Equals(storedValue, newValueStr, StringComparison.OrdinalIgnoreCase))
        {
            return false; // 字符串相同，没有变化
        }
        
        try
        {
            // 尝试解析存储的值
            var storedGridLength = GridLength.Parse(storedValue);
            
            // 对于像素值，比较数值是否有足够的差异
            if (storedGridLength.GridUnitType == GridUnitType.Pixel && newValue.GridUnitType == GridUnitType.Pixel)
            {
                // 如果差异小于0.5像素，认为没有变化
                return Math.Abs(storedGridLength.Value - newValue.Value) >= 0.5;
            }
            
            // 对于Star类型，比较是否有足够的差异
            if (storedGridLength.GridUnitType == GridUnitType.Star && newValue.GridUnitType == GridUnitType.Star)
            {
                // 对于比例值，如果差异小于0.01，认为没有变化
                return Math.Abs(storedGridLength.Value - newValue.Value) >= 0.01;
            }
            
            // 单位类型不同或其他情况，认为有变化
            return true;
        }
        catch
        {
            // 如果解析失败，认为有变化
            return true;
        }
    }
}
