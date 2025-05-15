﻿using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace MFAAvalonia.Helper.ValueType;

public partial class DragItemViewModel : ObservableObject
{
    public DragItemViewModel(MaaInterface.MaaInterfaceTask? interfaceItem)
    {
        InterfaceItem = interfaceItem;
        Name = interfaceItem?.Name ?? "未命名";
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    [ObservableProperty] private string _name = string.Empty;


    private bool? _isCheckedWithNull = false;
    private bool _isInitialized;

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the key is checked with null.
    /// </summary>
    [JsonIgnore]
    public bool? IsCheckedWithNull
    {
        get => _isCheckedWithNull;
        set
        {
            if (!_isInitialized)
            {
                _isInitialized = true;
                SetProperty(ref _isCheckedWithNull, value);
                if (InterfaceItem != null)
                    InterfaceItem.Check = IsChecked;
            }
            else
            {
                SetProperty(ref _isCheckedWithNull, value);
                if (InterfaceItem != null)
                    InterfaceItem.Check = _isCheckedWithNull;
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems,
                    Instances.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the key is checked.
    /// </summary>
    public bool IsChecked
    {
        get => IsCheckedWithNull != false;
        set => IsCheckedWithNull = value;
    }


    private bool _enableSetting;

    /// <summary>
    /// Gets or sets a value indicating whether gets or sets whether the setting enabled.
    /// </summary>
    [JsonIgnore]
    public bool EnableSetting
    {
        get => _enableSetting;
        set
        {
            SetProperty(ref _enableSetting, value);
            Instances.TaskQueueView.SetOption(this, value);
        }
    }

    private MaaInterface.MaaInterfaceTask? _interfaceItem;

    public MaaInterface.MaaInterfaceTask? InterfaceItem
    {
        get => _interfaceItem;
        set
        {
            if (value != null)
            {
                if (value.Name != null)
                    Name = value.Name;
                IsVisible = value is { Advanced.Count: > 0 } || value is { Option.Count: > 0 } || value.Repeatable == true || value.Document is { Count: > 0 };
                IsCheckedWithNull = value.Check;
            }

            SetProperty(ref _interfaceItem, value);
        }
    }

    [ObservableProperty] private bool _isVisible = true;


    private void UpdateContent()
    {
        if (!string.IsNullOrEmpty(InterfaceItem?.Name))
        {
            Name = LanguageHelper.GetLocalizedString(Name);
        }
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateContent();
    }

    /// <summary>
    /// Creates a deep copy of the current <see cref="DragItemViewModel"/> instance.
    /// </summary>
    /// <returns>A new <see cref="DragItemViewModel"/> instance that is a deep copy of the current instance.</returns>
    public DragItemViewModel Clone()
    {
        // Clone the InterfaceItem if it's not null
        MaaInterface.MaaInterfaceTask clonedInterfaceItem = InterfaceItem?.Clone();

        // Create a new DragItemViewModel instance with the cloned InterfaceItem
        DragItemViewModel clone = new(clonedInterfaceItem);

        // Copy all other properties to the new instance
        clone.Name = this.Name;
        clone.IsCheckedWithNull = this.IsCheckedWithNull;
        clone.EnableSetting = this.EnableSetting;

        return clone;
    }
}
