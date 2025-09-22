#region Usings

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

#endregion

namespace ScratchCardGenerator.Common.Converters
{
    #region Inverted Boolean to Visibility Converter

    /// <summary>
    /// A value converter that implements the IValueConverter interface to translate a boolean value
    /// into a System.Windows.Visibility enumeration value, but in reverse of the standard behaviour.
    /// This is a common requirement in MVVM applications for declaratively controlling UI element visibility from a ViewModel property.
    /// </summary>
    /// <remarks>
    /// This converter is essential for scenarios where a UI element should be hidden when a corresponding boolean property is true.
    /// For example, hiding a set of controls when a "IsPoundlandGame" flag is set to true.
    /// It promotes a clean separation of concerns by keeping this UI-specific logic out of the ViewModel.
    /// </remarks>
    public class InvertedBooleanToVisibilityConverter : IValueConverter
    {
        #region IValueConverter Implementation

        /// <summary>
        /// Converts a boolean value to its inverse System.Windows.Visibility representation.
        /// </summary>
        /// <param name="value">The source value being passed from the binding. This is expected to be a boolean.</param>
        /// <param name="targetType">The type of the binding target property. This is ignored in this implementation.</param>
        /// <param name="parameter">An optional converter parameter. This is ignored in this implementation.</param>
        /// <param name="culture">The culture to use in the converter. This is ignored in this implementation.</param>
        /// <returns>
        /// Returns <see cref="Visibility.Collapsed"/> if the input value is a boolean `true`.
        /// Returns <see cref="Visibility.Visible"/> for all other cases (including `false`, null, or non-boolean values).
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // We perform a robust check using 'is bool and true'.
            // This concisely checks if the value is not null, is of type bool, and has a value of true.
            // If the condition is met, the element should be hidden (Collapsed).
            // Otherwise, for false, null, or other types, we default to showing the element (Visible) to ensure a safe UI state.
            return value is bool and true ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Converts a System.Windows.Visibility value back to its inverse boolean representation.
        /// This method is not implemented as it is not required for the one-way data flows where this converter is typically used.
        /// </summary>
        /// <param name="value">The value that is produced by the binding target.</param>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="parameter">The converter parameter to use.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>This method will always throw a <see cref="NotImplementedException"/>.</returns>
        /// <exception cref="NotImplementedException">Thrown because converting from Visibility back to boolean is not supported.</exception>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // It is a best practice to explicitly throw this exception in ConvertBack for one-way converters.
            // This prevents accidental misuse and immediately alerts a developer if a two-way binding is attempted.
            throw new NotImplementedException();
        }

        #endregion
    }

    #endregion
}