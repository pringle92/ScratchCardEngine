#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Provides the abstract base class for all game types that can be placed on a scratch card.
    /// This class defines the common properties and behaviours that all game modules share, such as position, size,
    /// and selection state. It implements the INotifyPropertyChanged interface to support data binding in the WPF UI,
    /// allowing the user interface to update automatically when properties of a game module change.
    /// </summary>
    /// <remarks>
    /// The JsonPolymorphic and JsonDerivedType attributes are essential for correct serialisation and deserialisation
    /// of the different game module types when saving and loading project files. They tell the JSON serialiser how to
    /// handle the inheritance hierarchy, embedding a "GameType" discriminator property into the JSON to identify which
    /// concrete class to instantiate during deserialisation.
    /// </remarks>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "GameType")]
    [JsonDerivedType(typeof(MatchSymbolsInGridGame), typeDiscriminator: "MatchSymbolsInGrid")]
    [JsonDerivedType(typeof(FindWinningSymbolGame), typeDiscriminator: "FindWinningSymbol")]
    [JsonDerivedType(typeof(OnlineBonusGame), typeDiscriminator: "OnlineBonus")]
    [JsonDerivedType(typeof(MatchPrizesInRowGame), typeDiscriminator: "MatchPrizesInRow")]
    [JsonDerivedType(typeof(MatchSymbolsInRowGame), typeDiscriminator: "MatchSymbolsInRow")]
    [JsonDerivedType(typeof(MatchPrizesInGridGame), typeDiscriminator: "MatchPrizesInGrid")]
    [JsonDerivedType(typeof(ChristmasTreeGame), typeDiscriminator: "ChristmasTree")]
    [JsonDerivedType(typeof(MatchSymbolToPrizeGame), typeDiscriminator: "MatchSymbolToPrize")]
    public abstract class GameModule : INotifyPropertyChanged
    {
        #region Private Fields

        /// <summary>
        /// The backing field for the Position property.
        /// </summary>
        private Point _position;

        /// <summary>
        /// The backing field for the Size property, with a default value.
        /// </summary>
        private Size _size = new Size(150, 100);

        /// <summary>
        /// The backing field for the GameNumber property, with a default value.
        /// </summary>
        private int _gameNumber = 1;

        /// <summary>
        /// The backing field for the IsSelected property.
        /// </summary>
        private bool _isSelected;

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes. This is a core part of the INotifyPropertyChanged interface
        /// and is used by the WPF binding system to detect changes in the ViewModel/Model.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event to notify the UI and other subscribers that a property's value has changed.
        /// This method is virtual so that derived classes can override it if they have special requirements.
        /// </summary>
        /// <param name="propertyName">
        /// The name of the property that changed.
        /// The CallerMemberName attribute automatically provides the name of the calling property,
        /// removing the need to specify the property name as a magic string.
        /// </param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Invoke the event handler only if there are subscribers.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the descriptive name of the game module (e.g., "Match Symbols in Grid").
        /// This is displayed in the Game Module Palette in the UI.
        /// </summary>
        public string ModuleName { get; set; }

        /// <summary>
        /// Gets or sets the top-left position (X, Y coordinates) of the module on the design canvas.
        /// OnPropertyChanged is called to update the UI when this value changes, allowing the module to move visually.
        /// </summary>
        public Point Position
        {
            get => _position;
            set
            {
                _position = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the size (Width, Height) of the module on the design canvas.
        /// OnPropertyChanged is called to update the UI when this value changes, allowing the module to be resized.
        /// </summary>
        public Size Size
        {
            get => _size;
            set
            {
                _size = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets the designated game number for this module. This is used for ordering and for
        /// identification during file generation and analysis. It is typically managed by the ViewModel.
        /// </summary>
        public int GameNumber
        {
            get => _gameNumber;
            set
            {
                _gameNumber = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this module is currently selected in the designer.
        /// This property is used by UI triggers to provide visual feedback (e.g., a highlighted border).
        /// </summary>
        /// <remarks>
        /// This property is decorated with [JsonIgnore] to prevent it from being saved into the project file,
        /// as its state is purely for run-time UI purposes and should not persist between sessions.
        /// </remarks>
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// A read-only helper property used by XAML data triggers to identify if a module is of the OnlineBonusGame type.
        /// This allows for conditional UI rendering, such as showing a QR code instead of the default module text.
        /// </summary>
        [JsonIgnore]
        public bool IsOnlineBonusGame => this is OnlineBonusGame;

        /// <summary>
        /// A read-only helper property used by XAML data triggers to identify if a module is of the MatchSymbolToPrizeGame type.
        /// This allows the UI to conditionally show the dedicated symbols tab for this game in the properties pane.
        /// </summary>
        [JsonIgnore]
        public bool IsMatchSymbolToPrizeGame => this is MatchSymbolToPrizeGame;

        #endregion

        #region Abstract Methods for Gameplay Generation

        /// <summary>
        /// When overridden in a derived class, this method contains the core logic to generate the
        /// specific play data (symbols, prizes, etc.) for this game module on a given ticket.
        /// This is the primary method that defines the unique behaviour of each game type.
        /// </summary>
        /// <param name="ticket">The parent ticket object being generated. The resulting play data should be added to this ticket's GameData collection.</param>
        /// <param name="isWinningGame">A boolean indicating if this specific module instance should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game. This can be null for a losing game.</param>
        /// <param name="project">A reference to the entire scratch card project for accessing context like available symbols and prize tiers.</param>
        /// <param name="random">A shared Random instance to ensure variety and prevent predictable outcomes across the entire generation process.</param>
        public abstract void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random);

        #endregion

        #region Abstract Methods For File Generation

        /// <summary>
        /// When implemented in a derived class, gets the header names for this game's columns in the final CSV report.
        /// Each game type is responsible for defining its own unique column structure.
        /// </summary>
        /// <returns>A list of strings representing the unique column headers for this game module.</returns>
        public abstract List<string> GetCsvHeaders();

        /// <summary>
        /// When implemented in a derived class, gets the data for this game as a list of strings
        /// for writing to a single row in the final CSV report. The order of strings must match the order of headers from GetCsvHeaders.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data for this game.</param>
        /// <param name="project">The project context, used for looking up human-readable prize/symbol names from their IDs.</param>
        /// <returns>A list of strings representing the cell values for this game on a single ticket row.</returns>
        public abstract List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project);

        #endregion
    }
}