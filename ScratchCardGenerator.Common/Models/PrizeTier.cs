#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.ComponentModel;
using System.Runtime.CompilerServices;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Represents a single tier of prizes, including its monetary value, text representations,
    /// associated barcode, and the distribution counts for both Low-Value and High-Value winners.
    /// This class implements INotifyPropertyChanged to allow UI elements to update automatically when its properties change.
    /// </summary>
    public class PrizeTier : INotifyPropertyChanged
    {
        #region Private Backing Fields

        /// <summary>
        /// The backing field for the Id property.
        /// </summary>
        private int _id;

        /// <summary>
        /// The backing field for the Value property.
        /// </summary>
        private int _value;

        /// <summary>
        /// The backing field for the DisplayText property.
        /// </summary>
        private string _displayText;

        /// <summary>
        /// The backing field for the TextCode property.
        /// </summary>
        private string _textCode;

        /// <summary>
        /// The backing field for the Barcode property.
        /// </summary>
        private string _barcode;

        /// <summary>
        /// The backing field for the LvwWinnerCount property.
        /// </summary>
        private int _lvwWinnerCount;

        /// <summary>
        /// The backing field for the HvwWinnerCount property.
        /// </summary>
        private int _hvwWinnerCount;

        /// <summary>
        /// The backing field for the IsOnlinePrize property.
        /// </summary>
        private bool _isOnlinePrize;

        /// <summary>
        /// The backing field for the IsOnlineDrawOnly property.
        /// </summary>
        private bool _isOnlineDrawOnly;

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
        /// Gets or sets the unique numeric identifier for the prize tier.
        /// This is used internally to distinguish between prize tiers that might have the same value (e.g., an online and offline £10 prize).
        /// </summary>
        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the numeric value of the prize in pounds (e.g., 1000 for £1000).
        /// A value of 0 represents a losing ticket and is handled specially by the application.
        /// </summary>
        public int Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the text used for display purposes in the UI and on the scratch card (e.g., "£1,000", "£50").
        /// </summary>
        public string DisplayText
        {
            get => _displayText;
            set { _displayText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the codified, non-spaced, uppercase text for the prize (e.g., "ONETHOU", "FIFTY").
        /// This is critically important for linking prizes to symbols in the "Match Symbol to Prize" game.
        /// </summary>
        public string TextCode
        {
            get => _textCode;
            set { _textCode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the EAN-13 barcode associated with this prize tier.
        /// This barcode is revealed on a winning ticket and is used for validation and redemption.
        /// </summary>
        public string Barcode
        {
            get => _barcode;
            set { _barcode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the number of these prizes distributed per pack for Low-Value Winners (LVW).
        /// This defines the prize density in the common packs that form the bulk of the print run.
        /// </summary>
        public int LvwWinnerCount
        {
            get => _lvwWinnerCount;
            set { _lvwWinnerCount = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the total number of these prizes for the entire job for High-Value Winners (HVW).
        /// These are generated in a separate file and seeded into the final print run at random positions.
        /// </summary>
        public int HvwWinnerCount
        {
            get => _hvwWinnerCount;
            set { _hvwWinnerCount = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this prize is awarded via an online mechanism (e.g., a QR code).
        /// If true, this prize can only be won by an 'OnlineBonusGame' module.
        /// </summary>
        public bool IsOnlinePrize
        {
            get => _isOnlinePrize;
            set { _isOnlinePrize = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this prize tier is only available for separate, external online prize draws.
        /// If true, this prize tier will be completely excluded from the scratch card data generation.
        /// </summary>
        public bool IsOnlineDrawOnly
        {
            get => _isOnlineDrawOnly;
            set { _isOnlineDrawOnly = value; OnPropertyChanged(); }
        }

        #endregion
    }
}