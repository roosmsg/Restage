using System.Globalization;
using System.Windows.Data;

namespace Restage.Views;

/// <summary>
/// Two-way "is this value equal to the parameter" converter, for radio-style
/// choices bound to one property: each RadioButton compares against its own
/// ConverterParameter, and checking it writes that parameter back (coerced to
/// the property's type, so "60" can land in an int). Unchecking writes nothing —
/// the sibling that just got checked does the writing.
/// </summary>
public class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter != null
            ? System.Convert.ChangeType(parameter.ToString(), targetType, CultureInfo.InvariantCulture)
              ?? Binding.DoNothing
            : Binding.DoNothing;
}
