using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SukiUI.Converters;

public class IsBiggerThanZero : IValueConverter
{
    public static readonly IsBiggerThanZero Instance = new IsBiggerThanZero();
         
         
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if ((int)value > 0)
            return true;
             
        return false;
    }
         
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}