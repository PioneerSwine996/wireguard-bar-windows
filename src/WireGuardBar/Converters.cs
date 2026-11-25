using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WireGuardBar;

/// One converter for every "show/enable when" binding. Truthy: true, non-zero, non-empty, non-null.
/// Converts to bool or Visibility depending on the target. Parameter words: "not" inverts,
/// "hidden" uses Visibility.Hidden (keeps the space) instead of Collapsed.
public sealed class Is : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flags = parameter as string ?? "";
        var truthy = value switch
        {
            bool b => b,
            int i => i != 0,
            string s => s.Length > 0,
            ICollection c => c.Count > 0,
            null => false,
            _ => true,
        };
        if (flags.Contains("not")) truthy = !truthy;
        if (targetType == typeof(Visibility))
            return truthy ? Visibility.Visible : flags.Contains("hidden") ? Visibility.Hidden : Visibility.Collapsed;
        return truthy;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
