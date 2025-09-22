#region Usings

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

#endregion

namespace ScratchCardGenerator.Common.Converters
{
    #region Count to Visibility Converter

    /// <summary>
    /// A value converter that translates an integer count into a System.Windows.Visibility value.
    /// If the count is greater than zero, the element is visible; otherwise, it is collapsed.
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        #region IValueConverter Implementation

        /// <summary>
        /// Converts an integer value to a Visibility value.
        /// </summary>
        /// <param name="value">The integer count from the binding source.</param>
        /// <param name="targetType">The type of the binding target property (ignored).</param>
        /// <param name="parameter">The converter parameter (ignored).</param>
        /// <param name="culture">The culture to use in the converter (ignored).</param>
        /// <returns>
        /// Returns <see cref="Visibility.Visible"/> if the count is greater than 0.
        /// Returns <see cref="Visibility.Collapsed"/> for all other cases.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Check if the value is an integer and if it's greater than 0.
            if (value is int count && count > 0)
            {
                return Visibility.Visible;
            }
            // If the count is 0 or the value is not an integer, hide the element.
            return Visibility.Collapsed;
        }

        /// <summary>
        /// This method is not implemented as two-way binding is not required.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    #endregion
}