using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.Windows;
using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class VersionUpdateSettingsUserControlModel : ViewModelBase
{
    public enum UpdateProxyType
    {
        Http,
        Socks5
    }

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

    [ObservableProperty] private int _downloadSourceIndex = ConfigurationManager.Current.GetValue(ConfigurationKeys.DownloadSourceIndex, 1);

    partial void OnDownloadSourceIndexChanged(int value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.DownloadSourceIndex, value);
    }

    public ObservableCollection<LocalizationViewModel> UIUpdateChannelList =>
    [
        new("AlphaVersion"),
        new("BetaVersion"),
        new("StableVersion"),
    ];
    
    [ObservableProperty] private int _uIUpdateChannelIndex = ConfigurationManager.Current.GetValue(ConfigurationKeys.UIUpdateChannelIndex, 2);

    partial void OnUIUpdateChannelIndexChanged(int value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.UIUpdateChannelIndex, value);
    }
    public ObservableCollection<LocalizationViewModel> ResourceUpdateChannelList =>
    [
        new("AlphaVersion"),
        new("BetaVersion"),
        new("StableVersion"),
    ];
    
    [ObservableProperty] private int _resourceUpdateChannelIndex = ConfigurationManager.Current.GetValue(ConfigurationKeys.ResourceUpdateChannelIndex, 2);

    partial void OnResourceUpdateChannelIndexChanged(int value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.ResourceUpdateChannelIndex, value);
    }
    
    [ObservableProperty] private string _gitHubToken = SimpleEncryptionHelper.Decrypt(ConfigurationManager.Current.GetValue(ConfigurationKeys.GitHubToken, string.Empty));

    partial void OnGitHubTokenChanged(string value) => HandlePropertyChanged(ConfigurationKeys.GitHubToken, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _cdkPassword = SimpleEncryptionHelper.Decrypt(ConfigurationManager.Current.GetValue(ConfigurationKeys.DownloadCDK, string.Empty));

    partial void OnCdkPasswordChanged(string value) => HandlePropertyChanged(ConfigurationKeys.DownloadCDK, SimpleEncryptionHelper.Encrypt(value));

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
    [ObservableProperty] private string _proxyAddress = ConfigurationManager.Current.GetValue(ConfigurationKeys.ProxyAddress, string.Empty);
    [ObservableProperty] private UpdateProxyType _proxyType = ConfigurationManager.Current.GetValue(ConfigurationKeys.ProxyType, UpdateProxyType.Http, UpdateProxyType.Http, new UniversalEnumConverter<UpdateProxyType>());
    public ObservableCollection<LocalizationViewModel> ProxyTypeList =>
    [
        new("HTTP Proxy")
        {
            Other = UpdateProxyType.Http
        },
        new("SOCKS5 Proxy")
        {
            Other = UpdateProxyType.Socks5
        },
    ];

    partial void OnProxyAddressChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ProxyAddress, value);

    partial void OnProxyTypeChanged(UpdateProxyType value) => HandlePropertyChanged(ConfigurationKeys.ProxyType, value.ToString());

    [RelayCommand]
    private void UpdateResource()
    {
        VersionChecker.UpdateResourceAsync();
    }
    
    [RelayCommand]
    private void RedownloadResource()
    {
        VersionChecker.UpdateResourceAsync("v0.0.0");
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
