#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.Collections.Generic;
using System.Collections.ObjectModel;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Represents the top-level container for an entire scratch card job.
    /// This class encapsulates all settings, prizes, symbols, and the card layout.
    /// It is designed to be serialised to and from a file (typically JSON) for saving and loading projects.
    /// </summary>
    public class ScratchCardProject
    {
        #region Properties

        /// <summary>
        /// Gets or sets the file path associated with the project on disk.
        /// This is not saved to the project file itself but is managed at runtime by the ViewModel
        /// to know where to save changes.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the core job settings, such as job number, client name, and ticket quantities.
        /// </summary>
        public JobSettings Settings { get; set; }

        /// <summary>
        /// Gets or sets the security-related settings, primarily the paths to the security code files.
        /// </summary>
        public SecuritySettings Security { get; set; }

        /// <summary>
        /// Gets or sets the list of all possible prize tiers for this job.
        /// An ObservableCollection is used to allow the UI to automatically update when prizes are added or removed.
        /// </summary>
        public ObservableCollection<PrizeTier> PrizeTiers { get; set; }

        /// <summary>
        /// Gets or sets the list of all available standard symbols that can be used in most game types.
        /// </summary>
        public ObservableCollection<Symbol> AvailableSymbols { get; set; }

        /// <summary>
        /// Gets or sets the dedicated list of numeric symbols to be used by all "Match Symbol to Prize" games.
        /// This keeps them separate from the standard graphical symbols.
        /// </summary>
        public ObservableCollection<Symbol> NumericSymbols { get; set; }

        /// <summary>
        /// Gets or sets the card layout, which contains the collection of all configured game modules.
        /// </summary>
        public CardLayout Layout { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="ScratchCardProject"/> class with default objects.
        /// This is crucial for preventing null reference exceptions when a new, empty project is created.
        /// </summary>
        public ScratchCardProject()
        {
            // Initialise all properties with new instances to ensure the project is in a valid state from the start.
            FilePath = string.Empty;
            Settings = new JobSettings();
            Security = new SecuritySettings();
            PrizeTiers = new ObservableCollection<PrizeTier>();
            AvailableSymbols = new ObservableCollection<Symbol>();
            NumericSymbols = new ObservableCollection<Symbol>(); // Initialise the new numeric symbols collection.
            Layout = new CardLayout();
        }

        #endregion
    }
}