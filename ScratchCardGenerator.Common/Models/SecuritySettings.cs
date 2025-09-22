#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Holds the configuration related to the external security code files used in data generation.
    /// This class implements INotifyPropertyChanged to support live UI updates when file paths are changed.
    /// </summary>
    public class SecuritySettings : INotifyPropertyChanged
    {
        #region Private Fields

        /// <summary>
        /// The backing field for the ThreeDigitCodeFilePath property.
        /// </summary>
        private string _threeDigitCodeFilePath;

        /// <summary>
        /// The backing field for the SevenDigitCodeFilePath property.
        /// </summary>
        private string _sevenDigitCodeFilePath;

        /// <summary>
        /// The backing field for the SixDigitCodeFilePath property.
        /// </summary>
        private string _sixDigitCodeFilePath;

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes, used by the WPF binding system.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event to notify the UI that a property's value has changed.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed. This is automatically provided by the CallerMemberName attribute.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the full file path to the 3-digit security code file (.rnd).
        /// This is used in one of the two possible security code generation schemes.
        /// </summary>
        public string ThreeDigitCodeFilePath
        {
            get => _threeDigitCodeFilePath;
            set
            {
                // Store path as lowercase for consistency and to avoid potential case-sensitivity issues.
                _threeDigitCodeFilePath = value?.ToLower();
                OnPropertyChanged();
                // Notify the UI that the related helper property may have also changed.
                OnPropertyChanged(nameof(HasThreeAndSevenDigitFiles));
            }
        }

        /// <summary>
        /// Gets or sets the full file path to the 7-digit security code file (.rnd).
        /// This is used in conjunction with the 3-digit file.
        /// </summary>
        public string SevenDigitCodeFilePath
        {
            get => _sevenDigitCodeFilePath;
            set
            {
                _sevenDigitCodeFilePath = value?.ToLower();
                OnPropertyChanged();
                // Notify the UI that the related helper property may have also changed.
                OnPropertyChanged(nameof(HasThreeAndSevenDigitFiles));
            }
        }

        /// <summary>
        /// Gets or sets the full file path to the 6-digit security code file (.rnd).
        /// This file provides an alternative, standalone method for generating security codes.
        /// Its presence will disable the 3 and 7-digit file inputs in the UI.
        /// </summary>
        public string SixDigitCodeFilePath
        {
            get => _sixDigitCodeFilePath;
            set
            {
                _sixDigitCodeFilePath = value?.ToLower();
                OnPropertyChanged();
                // Notify the UI that the related helper property may have also changed.
                OnPropertyChanged(nameof(HasSixDigitFile));
            }
        }

        #endregion

        #region Helper Properties for UI Logic

        /// <summary>
        /// A read-only helper property that returns true if the 6-digit file path has been set.
        /// This is used by XAML triggers to disable the 3 and 7-digit controls, enforcing the
        /// mutual exclusivity of the two security code schemes.
        /// </summary>
        /// <remarks>
        /// This property is ignored during JSON serialisation as its value is derived at runtime.
        /// </remarks>
        [JsonIgnore]
        public bool HasSixDigitFile => !string.IsNullOrEmpty(SixDigitCodeFilePath);

        /// <summary>
        /// A read-only helper property that returns true if either the 3 or 7-digit file path has been set.
        /// This is used by XAML triggers to disable the 6-digit control.
        /// </summary>
        /// <remarks>
        /// This property is ignored during JSON serialisation as its value is derived at runtime.
        /// </remarks>
        [JsonIgnore]
        public bool HasThreeAndSevenDigitFiles => !string.IsNullOrEmpty(ThreeDigitCodeFilePath) || !string.IsNullOrEmpty(SevenDigitCodeFilePath);

        #endregion
    }
}