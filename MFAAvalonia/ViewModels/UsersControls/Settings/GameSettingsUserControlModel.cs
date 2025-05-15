using CommunityToolkit.Mvvm.ComponentModel;
using MaaFramework.Binding;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class GameSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty] private bool _enableRecording = ConfigurationManager.Maa.GetValue(ConfigurationKeys.Recording, false);

    [ObservableProperty] private bool _enableSaveDraw = ConfigurationManager.Maa.GetValue(ConfigurationKeys.SaveDraw, false);

    [ObservableProperty] private bool _showHitDraw = ConfigurationManager.Maa.GetValue(ConfigurationKeys.ShowHitDraw, false);

    [ObservableProperty] private string _prescript = ConfigurationManager.Current.GetValue(ConfigurationKeys.Prescript, string.Empty);

    [ObservableProperty] private string _postScript = ConfigurationManager.Current.GetValue(ConfigurationKeys.Postscript, string.Empty);


    partial void OnEnableRecordingChanged(bool value)
    {
        ConfigurationManager.Maa.SetValue(ConfigurationKeys.Recording, value);
        MaaProcessor.Utility.SetOptionRecording(value);
        Instances.RootViewModel.IsDebugMode = EnableSaveDraw || EnableRecording || ShowHitDraw;
        MaaProcessor.Instance.SetTasker();
    }

    partial void OnEnableSaveDrawChanged(bool value)
    {
        ConfigurationManager.Maa.SetValue(ConfigurationKeys.SaveDraw, value);
        MaaProcessor.Utility.SetOptionSaveDraw(value);
        Instances.RootViewModel.IsDebugMode = EnableSaveDraw || EnableRecording || ShowHitDraw;
        MaaProcessor.Instance.SetTasker();
    }

    partial void OnShowHitDrawChanged(bool value)
    {
        ConfigurationManager.Maa.SetValue(ConfigurationKeys.ShowHitDraw, value);
        MaaProcessor.Utility.SetOptionShowHitDraw(value);
        Instances.RootViewModel.IsDebugMode = EnableSaveDraw || EnableRecording || ShowHitDraw;
        MaaProcessor.Instance.SetTasker();
    }

    partial void OnPrescriptChanged(string value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.Prescript, value);
    }

    partial void OnPostScriptChanged(string value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.Postscript, value);
    }
    //
    // [ObservableProperty] private ObservableCollection<MaaInterface.MaaCustomResource> _currentResources = [];
    //
    // [ObservableProperty] private string _currentResource = ConfigurationHelper.GetValue(ConfigurationKeys.Resource, string.Empty);
    //
    // partial void OnCurrentResourceChanged(string value)
    // {
    //     ConfigurationHelper.SetValue(ConfigurationKeys.Resource, value);
    // }
}
