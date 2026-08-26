using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Awizzy.Core.Models;

namespace Awizzy.App.Converters;

public class SessionStateToChipBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Inactive = new(Color.Parse("#64748B"));
    private static readonly SolidColorBrush Busy = new(Color.Parse("#2563EB"));
    private static readonly SolidColorBrush Active = new(Color.Parse("#16A34A"));
    private static readonly SolidColorBrush Error = new(Color.Parse("#DC2626"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            SessionState.Active => Active,
            SessionState.Starting or SessionState.Refreshing => Busy,
            SessionState.Error => Error,
            _ => Inactive,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
