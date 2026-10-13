using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SQLRestoreSOS.Core.Restore;

namespace SQLRestoreSOS.App.ViewModels;

public sealed class SeverityToBrushConverter : IValueConverter
{
    public static readonly SeverityToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RestoreProgressSeverity.Error => Brushes.OrangeRed,
        RestoreProgressSeverity.Warning => Brushes.Orange,
        _ => Brushes.Gainsboro
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
