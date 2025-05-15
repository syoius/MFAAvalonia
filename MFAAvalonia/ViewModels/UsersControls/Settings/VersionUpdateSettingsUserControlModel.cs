using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Windows;
using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class VersionUpdateSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty] private string _maaFwVersion = MaaProcessor.Utility.Version;
    [ObservableProperty] private string _mfaVersion = RootViewModel.Version;
    [ObservableProperty] private string _resourceVersion = string.Empty;
    [ObservableProperty] private bool _showResourceVersion;
    partial void OnResourceVersionChanged(string value)
    {
        ShowResourceVersion = !string.IsNullOrWhiteSpace(value);
    }

    public ObservableCollection<LocalizationViewModel> DownloadSourceList =>
    [
        new()
        {
            Name = "GitHub"
        },
        new("MirrorChyan"),
    ];

    [ObservableProperty] private int _downloadSourceIndex = ConfigurationManager.Current.GetValue(ConfigurationKeys.DownloadSourceIndex, 0);

    partial void OnDownloadSourceIndexChanged(int value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.DownloadSourceIndex, value);
    }

    [ObservableProperty] private string _gitHubToken = SimpleEncryptionHelper.Decrypt(ConfigurationManager.Current.GetValue(ConfigurationKeys.GitHubToken, string.Empty));

    partial void OnGitHubTokenChanged(string value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.GitHubToken, SimpleEncryptionHelper.Encrypt(value));
    }
    
    [ObservableProperty] private string _cdkPassword = SimpleEncryptionHelper.Decrypt(ConfigurationManager.Current.GetValue(ConfigurationKeys.DownloadCDK, string.Empty));

    partial void OnCdkPasswordChanged(string value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.DownloadCDK, SimpleEncryptionHelper.Encrypt(value));
    }

    [ObservableProperty] private bool _enableCheckVersion = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableCheckVersion, true);

    [ObservableProperty] private bool _enableAutoUpdateResource = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableAutoUpdateResource, false);

    [ObservableProperty] private bool _enableAutoUpdateMFA = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableAutoUpdateMFA, false);

    partial void OnEnableCheckVersionChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableCheckVersion, value);
    }

    partial void OnEnableAutoUpdateResourceChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableAutoUpdateResource, value);
    }

    partial void OnEnableAutoUpdateMFAChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableAutoUpdateMFA, value);
    }

    [RelayCommand]
    private void UpdateResource()
    {
        VersionChecker.UpdateResourceAsync();
    }
    [RelayCommand]
    private void CheckResourceUpdate()
    {
        VersionChecker.CheckResourceVersionAsync();
    }
    [RelayCommand]
    private void UpdateMFA()
    {
        VersionChecker.UpdateMFAAsync();
    }
    [RelayCommand]
    private void CheckMFAUpdate()
    {
        VersionChecker.CheckMFAVersionAsync();
    }
    [RelayCommand]
    private void UpdateMaaFW()
    {
        VersionChecker.UpdateMaaFwAsync();
    }
}
