#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    // NEW: An enum to define the available weighting options for near-miss generation.
    // This provides a type-safe way to handle the setting.
    #region NearMissWeighting Enum

    /// <summary>
    /// Defines the weighting preference for generating near-misses on losing tickets.
    /// </summary>
    public enum NearMissWeighting
    {
        /// <summary>
        /// Favours generating a lower number of near-miss sets (e.g., mostly just one).
        /// </summary>
        [Description("Low (Fewer near-misses)")]
        Low,
        /// <summary>
        /// Gives an equal chance to all possible numbers of near-miss sets.
        /// </summary>
        [Description("Balanced (Even chance)")]
        Balanced,
        /// <summary>
        /// Favours generating a higher number of near-miss sets, making losing tickets feel "closer".
        /// </summary>
        [Description("High (More near-misses)")]
        High
    }

    #endregion

    /// <summary>
    /// Encapsulates the core configuration and settings for a scratch card job.
    /// This class holds high-level information like job numbers, client details, and ticket quantities.
    /// It implements INotifyPropertyChanged to support live UI updates and IDataErrorInfo for real-time input validation in the UI.
    /// </summary>
    public class JobSettings : INotifyPropertyChanged, IDataErrorInfo
    {
        #region Private Backing Fields

        private string _jobNo = "";
        private string _subJob = "";
        private string _jobName = "";
        private string _jobCode = "";
        private string _client = "";
        private string _productBarcode = "";
        private string _loserBarcode = "";
        private bool _gamesAware = false;
        private int _noArtWorks = 1;
        private int _cardsPerPack;
        private int _noOut;
        private int _totalPacks;
        private int _noComPack;
        private decimal _ticketSalePrice = 1.00m;
        private DateTime _endDate = DateTime.Now.AddYears(1);

        // --- POUNDLAND FIELDS ---
        private bool _isPoundlandGame = false;
        private string _poundlandBarcodePrefix = "310";

        // Backing field for the new weighting property. Defaults to High to match old behaviour.
        private NearMissWeighting _losingTicketNearMissWeighting = NearMissWeighting.High;

        // Backing field for the high-value near-miss preference.
        private bool _favourHighValueNearMisses = false;

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

        #region IDataErrorInfo Implementation

        /// <summary>
        /// Gets an error message indicating what is wrong with this object.
        /// For this implementation, we handle errors on a per-property basis, so this is not used.
        /// </summary>
        [Browsable(false)] // Hides this property from UI designers.
        public string Error => null;

        /// <summary>
        /// Gets the error message for the property with the given name.
        /// This is called by the WPF binding engine when a binding has ValidatesOnDataErrors=True.
        /// </summary>
        /// <param name="columnName">The name of the property to validate.</param>
        /// <returns>The error message for the property if it is invalid; otherwise, null or an empty string.</returns>
        [Browsable(false)]
        public string this[string columnName]
        {
            get
            {
                string result = null;
                // Use a switch statement to provide validation logic for specific properties.
                switch (columnName)
                {
                    case nameof(CardsPerPack):
                        if (CardsPerPack <= 0)
                            result = "Cards per Pack must be greater than 0.";
                        break;
                    case nameof(TotalPacks):
                        if (TotalPacks <= 0)
                            result = "Total Packs must be greater than 0.";
                        break;
                    case nameof(NoOut):
                        if (NoOut < 0)
                            result = "No. Out cannot be negative.";
                        break;
                    case nameof(NoComPack):
                        if (NoComPack < 0)
                            result = "No. of Common Packs cannot be negative.";
                        break;
                    case nameof(TicketSalePrice):
                        if (TicketSalePrice < 0)
                            result = "Ticket Price cannot be negative.";
                        break;
                }
                return result;
            }
        }

        #endregion

        #region Job Identification

        /// <summary>
        /// Gets or sets the main job number.
        /// </summary>
        public string JobNo
        {
            get => _jobNo;
            set { _jobNo = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the sub-job identifier, if any.
        /// </summary>
        public string SubJob
        {
            get => _subJob;
            set { _subJob = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the descriptive name of the job.
        /// </summary>
        public string JobName
        {
            get => _jobName;
            set { _jobName = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the job code, often used for file naming and internal referencing.
        /// </summary>
        public string JobCode
        {
            get => _jobCode;
            set { _jobCode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the name of the client for whom the job is being produced.
        /// </summary>
        public string Client
        {
            get => _client;
            set { _client = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the main product barcode for the entire job.
        /// </summary>
        public string ProductBarcode
        {
            get => _productBarcode;
            set { _productBarcode = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the barcode specifically for non-winning (loser) tickets.
        /// </summary>
        public string LoserBarcode
        {
            get => _loserBarcode;
            set { _loserBarcode = value; OnPropertyChanged(); }
        }

        #endregion

        #region Game Configuration

        /// <summary>
        /// Gets or sets a value indicating whether different games on the same ticket
        /// should be aware of each other's symbols to prevent certain combinations (e.g., near-misses).
        /// </summary>
        public bool GamesAware
        {
            get => _gamesAware;
            set { _gamesAware = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the number of artworks. In this application's context, this is typically always 1.
        /// </summary>
        public int NoArtWorks
        {
            get => _noArtWorks;
            set { _noArtWorks = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the weighting preference for generating near-misses on losing tickets.
        /// This controls the "feel" of the losing tickets.
        /// </summary>
        public NearMissWeighting LosingTicketNearMissWeighting
        {
            get => _losingTicketNearMissWeighting;
            set { _losingTicketNearMissWeighting = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the near-miss generation should
        /// prioritise using items from high-value prize tiers.
        /// </summary>
        public bool FavourHighValueNearMisses
        {
            get => _favourHighValueNearMisses;
            set { _favourHighValueNearMisses = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the number of individual scratch cards in a single pack.
        /// </summary>
        public int CardsPerPack
        {
            get => _cardsPerPack;
            set
            {
                _cardsPerPack = value;
                OnPropertyChanged();
                // When this value changes, notify the UI that dependent calculated properties also need to be refreshed.
                OnPropertyChanged(nameof(TotalTickets));
                OnPropertyChanged(nameof(TotalCommonTickets));
            }
        }

        /// <summary>
        /// Gets or sets the number of "outs" or blocks, a printing industry term used for calculation purposes.
        /// This typically refers to the number of items printed on a single sheet.
        /// </summary>
        public int NoOut
        {
            get => _noOut;
            set
            {
                _noOut = value;
                OnPropertyChanged();
                // Notify dependent properties of the change.
                OnPropertyChanged(nameof(PrintPacks));
                OnPropertyChanged(nameof(TotalTickets));
            }
        }

        /// <summary>
        /// Gets or sets the total number of "live" packs intended for public sale.
        /// </summary>
        public int TotalPacks
        {
            get => _totalPacks;
            set
            {
                _totalPacks = value;
                OnPropertyChanged();
                // Notify dependent properties of the change.
                OnPropertyChanged(nameof(PrintPacks));
                OnPropertyChanged(nameof(TotalTickets));
            }
        }

        /// <summary>
        /// Gets or sets the number of "common packs" to generate. These packs form the basis of the
        /// Low-Value Winner (LVW) data file and are repeated to construct the full print run.
        /// </summary>
        public int NoComPack
        {
            get => _noComPack;
            set
            {
                _noComPack = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalCommonTickets));
            }
        }

        /// <summary>
        /// Gets or sets the redemption end date for the scratch card game.
        /// </summary>
        public DateTime EndDate
        {
            get => _endDate;
            set { _endDate = value; OnPropertyChanged(); }
        }

        #endregion

        #region Poundland Configuration (NEW)

        /// <summary>
        /// Gets or sets a value indicating whether this job should use the special Poundland generation logic.
        /// </summary>
        public bool IsPoundlandGame
        {
            get => _isPoundlandGame;
            set { _isPoundlandGame = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the configurable prefix used in the Poundland barcode generation formula.
        /// </summary>
        public string PoundlandBarcodePrefix
        {
            get => _poundlandBarcodePrefix;
            set { _poundlandBarcodePrefix = value; OnPropertyChanged(); }
        }

        #endregion

        #region Financials

        /// <summary>
        /// Gets or sets the sale price of a single ticket.
        /// This is used to calculate total potential revenue and payout percentages.
        /// </summary>
        public decimal TicketSalePrice
        {
            get => _ticketSalePrice;
            set { _ticketSalePrice = value; OnPropertyChanged(); }
        }

        #endregion

        #region Calculated Properties

        /// <summary>
        /// Gets the calculated total number of packs that need to be printed, including extras for setup and spares.
        /// The calculation is based on a specific formula from the original legacy application.
        /// </summary>
        public int PrintPacks
        {
            get
            {
                if (NoOut <= 0) return 0;
                // 1. Calculate the number of packs per "out".
                int packsPerOut = (int)Math.Ceiling((double)TotalPacks / NoOut);

                // 2. The result must be an even number.
                if (packsPerOut % 2 != 0)
                {
                    packsPerOut++;
                }

                // 3. Add 6 extra blocks/packs for printing setup and waste.
                packsPerOut += 6;

                return packsPerOut * NoOut;
            }
        }

        /// <summary>
        /// Gets the total number of tickets to be printed across all packs (PrintPacks * CardsPerPack).
        /// </summary>
        public int TotalTickets => PrintPacks * CardsPerPack;

        /// <summary>
        /// Gets the total number of tickets across all generated common packs (NoComPack * CardsPerPack).
        /// </summary>
        public int TotalCommonTickets => NoComPack * CardsPerPack;

        #endregion
    }
}
