using System;
using System.Globalization;
using System.Windows.Data;

namespace ScratchCardGenerator.Common.Converters
{
    /// <summary>
    /// A value converter that converts any object to a boolean value.
    /// It returns true if the object is not null, and false if it is null.
    /// This is useful for data triggers that need to react to whether an item is selected.
    /// </summary>
    public class ObjectToBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Converts an object to a boolean.
        /// </summary>
        /// <param name="value">The object to convert.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">The converter parameter to use.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>True if the value is not null; otherwise, false.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        /// <summary>
        /// This method is not implemented and will throw an exception if called.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
