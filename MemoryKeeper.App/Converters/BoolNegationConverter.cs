using Microsoft.UI.Xaml.Data;

namespace MemoryKeeper.App.Converters;

public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool flag && !flag;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool flag && !flag;
    }
}
