using System;
using System.Globalization;
using System.Windows.Data;

namespace ScratchCardGenerator.Common.Converters
{
    public class EnumValueToInstanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var items = parameter as System.Collections.IEnumerable;
            if (items == null || value == null)
                return null;

            foreach (var item in items)
            {
                if (item.Equals(value))
                    return item;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
