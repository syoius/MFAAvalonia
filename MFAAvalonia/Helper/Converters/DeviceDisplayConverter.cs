using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Templates;
using FluentAvalonia.UI.Controls;
using MaaFramework.Binding;
using System;
using System.Globalization;

namespace MFAAvalonia.Helper.Converters;

public class DeviceDisplayConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AdbDeviceInfo device)
            return $"{device.AdbSerial} ({device.Name})";

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo cultureInfo) => throw new NotSupportedException();
}
