using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MFAAvalonia.ViewModels;

public class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        Initialize();
    }
    
    protected virtual void Initialize() { }
    
    protected void HandlePropertyChanged<T>(string configKey, T newValue, Action<T>? action = null)
    {
        ConfigurationManager.Current.SetValue(configKey, newValue);
        action?.Invoke(newValue);
    }
    
    protected void HandleStringPropertyChanged<T>(string configKey, T newValue, Action<T>? action = null)
    {
        ConfigurationManager.Current.SetValue(configKey, newValue.ToString());
        action?.Invoke(newValue);
    }
    
    protected void HandlePropertyChanged<T>(string configKey, T newValue, Action? action)
    {
        ConfigurationManager.Current.SetValue(configKey, newValue);
        action?.Invoke();
    }
    
    protected bool? SetNewProperty<T>([NotNullIfNotNull(nameof(newValue))] ref T field,
        T newValue,
        [CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanging(propertyName);

        field = newValue;

        OnPropertyChanged(propertyName);

        return true;
    }
}
