using System;
using System.Globalization;
using Avalonia.Controls;
using MFAAvalonia.Configuration;

namespace MFAAvalonia.Helper;

public static class GridLengthStorage
{
    public static GridLength Load(string key, GridLength fallback)
    {
        var config = ConfigurationManager.Current;
        if (config.TryGetValue<GridLength>(key, out var value))
        {
            return value;
        }

        var raw = config.GetValue(key, string.Empty);
        return TryParse(raw, out var parsed) ? parsed : fallback;
    }

    public static void Save(string key, GridLength value)
    {
        ConfigurationManager.Current.SetValue(key, Serialize(value));
    }

    private static string Serialize(GridLength value)
    {
        return value.GridUnitType switch
        {
            GridUnitType.Auto => "Auto",
            GridUnitType.Star => FormattableString.Invariant($"Star:{value.Value}"),
            GridUnitType.Pixel => FormattableString.Invariant($"Pixel:{value.Value}"),
            _ => FormattableString.Invariant($"Pixel:{value.Value}")
        };
    }

    private static bool TryParse(string? raw, out GridLength length)
    {
        length = GridLength.Auto;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            length = GridLength.Auto;
            return true;
        }

        if (text.EndsWith("*", StringComparison.Ordinal))
        {
            var number = text.TrimEnd('*');
            var value = 1d;
            if (!string.IsNullOrWhiteSpace(number)
                && !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            length = new GridLength(value, GridUnitType.Star);
            return true;
        }

        var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            if (parts[0].Equals("Star", StringComparison.OrdinalIgnoreCase))
            {
                length = new GridLength(number, GridUnitType.Star);
                return true;
            }

            if (parts[0].Equals("Pixel", StringComparison.OrdinalIgnoreCase))
            {
                length = new GridLength(number, GridUnitType.Pixel);
                return true;
            }

            if (parts[0].Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                length = GridLength.Auto;
                return true;
            }
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
        {
            length = new GridLength(pixels, GridUnitType.Pixel);
            return true;
        }

        return false;
    }
}
