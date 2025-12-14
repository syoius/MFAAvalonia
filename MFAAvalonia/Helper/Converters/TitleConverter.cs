using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MFAAvalonia.Helper.Converters;

public class TitleConverter : MarkupExtension, IMultiValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var customTitle = SafeGetValue<string>(values, 0);
        var isCustomVisible = SafeGetValue<bool>(values, 1);
        var appName = SafeGetValue<object>(values, 2)?.ToString() ?? string.Empty;
        var appVersion = SafeGetValue<string>(values, 3) ?? string.Empty;
        var resourceName = SafeGetValue<string>(values, 4);
        var resourceVersion = SafeGetValue<string>(values, 5);
        var isResourceVisible = SafeGetValue<bool>(values, 6);
        var currentConfig = SafeGetValue<string>(values, 7);

        var parts = new List<string>();
        var hasCustomTitle = isCustomVisible && !string.IsNullOrWhiteSpace(customTitle);

        if (hasCustomTitle)
        {
            parts.Add(customTitle!.Trim());

            if (!string.IsNullOrWhiteSpace(resourceVersion))
                parts.Add(resourceVersion!.Trim());
        }
        else
        {
            var baseTitle = string.IsNullOrWhiteSpace(appVersion)
                ? appName
                : $"{appName} {appVersion}".Trim();
            if (!string.IsNullOrWhiteSpace(baseTitle))
                parts.Add(baseTitle);

            if (isResourceVisible && !string.IsNullOrWhiteSpace(resourceName))
            {
                var resourcePart = resourceName!.Trim();
                if (!string.IsNullOrWhiteSpace(resourceVersion))
                    resourcePart = $"{resourcePart} {resourceVersion!.Trim()}".Trim();
                if (!string.IsNullOrWhiteSpace(resourcePart))
                    parts.Add(resourcePart);
            }
            else if (!string.IsNullOrWhiteSpace(resourceVersion))
            {
                parts.Add(resourceVersion!.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(currentConfig))
        {
            var configLabel = LangKeys.CurrentConfig.ToLocalization();
            var configIcon = "✏️";
            var configText = string.IsNullOrWhiteSpace(configLabel)
                ? $"{configIcon} {currentConfig}".Trim()
                : $"{configIcon} {configLabel}: {currentConfig}".Trim();
            parts.Add(configText);
        }

        return string.Join("  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 安全获取绑定值（处理 UnsetValue 和类型转换）
    /// </summary>
    private T SafeGetValue<T>(IList<object?> values, int index, T defaultValue = default)
    {
        if (index >= values.Count) return defaultValue;

        var value = values[index];

        // 处理 Avalonia 的 UnsetValue
        if (value is UnsetValueType || value == null) return defaultValue;

        try
        {
            return (T)System.Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}
