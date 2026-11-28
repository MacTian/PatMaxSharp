using System;
using System.Globalization;
using System.Windows.Data;

namespace ShapeAnchor.UI;

/// <summary>Inverts a boolean, so a control can be enabled when a flag is off.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}