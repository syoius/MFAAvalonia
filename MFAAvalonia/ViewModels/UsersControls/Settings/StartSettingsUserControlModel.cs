using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class StartSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty] private bool _autoMinimize = ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoMinimize, false);

    [ObservableProperty] private bool _autoHide = ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoHide, false);

    [ObservableProperty] private string _softwarePath = ConfigurationManager.Current.GetValue(ConfigurationKeys.SoftwarePath, string.Empty);
    
    [ObservableProperty] private string _emulatorConfig = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);

    [ObservableProperty] private double _waitSoftwareTime = ConfigurationManager.Current.GetValue(ConfigurationKeys.WaitSoftwareTime, 60.0);


    partial void OnAutoMinimizeChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.AutoMinimize, value);
    }

    partial void OnAutoHideChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.AutoHide, value);
    }

    partial void OnSoftwarePathChanged(string value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.SoftwarePath, value);
    }

    partial void OnEmulatorConfigChanged(string value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.EmulatorConfig, value);
    }

    partial void OnWaitSoftwareTimeChanged(double value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.WaitSoftwareTime, value);
    }
    
    [RelayCommand]
    async private Task SelectSoft()
    {
        var storageProvider = Instances.RootView.StorageProvider;

        // 配置文件选择器选项
        var options = new FilePickerOpenOptions
        {
            Title = "SelectExecutableFile".ToLocalization(),
            FileTypeFilter =
            [
                new FilePickerFileType("ExeFilter".ToLocalization())
                {
                    Patterns = ["*"] // 支持所有文件类型
                }
            ]
        };
        
        var result = await storageProvider.OpenFilePickerAsync(options);

        // 处理选择结果
        if (result is { Count: > 0 } && result[0].TryGetLocalPath() is { } path)
        {
            SoftwarePath = path;
        }
    }
}
