#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.ComponentModel;
using System.Runtime.CompilerServices;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Represents a single playable symbol in a game, such as a "Gold Bar" or a numeric value.
    /// This class implements INotifyPropertyChanged to allow the UI to update automatically
    /// when its properties are changed by the user in the data grid.
    /// </summary>
    public class Symbol : INotifyPropertyChanged
    {
        #region Private Backing Fields

        /// <summary>
        /// The backing field for the Id property.
        /// </summary>
        private int _id;

        /// <summary>
        /// The backing field for the Name property.
        /// </summary>
        private string _name;

        /// <summary>
        /// The backing field for the DisplayText property.
        /// </summary>
        private string _displayText;

        /// <summary>
        /// The backing field for the ImagePath property.
        /// </summary>
        private string _imagePath;

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
        /// Gets or sets the unique numeric identifier for the symbol.
        /// This ID is what is stored in the generated ticket data.
        /// </summary>
        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the internal, non-spaced, uppercase name of the symbol (e.g., "GOLDBAR").
        /// This property is set automatically when DisplayText changes and is used for logical comparisons,
        /// such as linking a symbol to a prize via its TextCode.
        /// </summary>
        public string Name
        {
            get => DisplayText?.ToUpperInvariant().Replace(" ", "");
            private set { _name = value; OnPropertyChanged(); } // The setter is private to enforce derivation from DisplayText.
        }

        /// <summary>
        // Gets or sets the human-readable text for the symbol (e.g., "Gold Bar").
        // This is what the user edits in the UI. Changing this value will automatically update the internal 'Name' property.
        /// </summary>
        public string DisplayText
        {
            get => _displayText;
            set
            {
                _displayText = value;
                OnPropertyChanged();

                // Automatically update the internal name based on the display text.
                // This creates a consistent internal name for logic purposes and ensures data integrity.
                // We raise the PropertyChanged event for 'Name' as well, in case any UI element is bound to it.
                OnPropertyChanged(nameof(Name));
            }
        }

        /// <summary>
        /// Gets or sets the optional file path to an image (e.g., a PDF) representing the symbol's artwork.
        /// </summary>
        public string ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(); }
        }

        #endregion
    }
}