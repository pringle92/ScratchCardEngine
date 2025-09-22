#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.Collections.Generic;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Represents the generated play data for a single game module on a single ticket.
    /// This class holds the symbols and any associated prize information that a game module produces.
    /// A collection of these objects is stored in the parent Ticket object.
    /// </summary>
    public class GamePlayData
    {
        #region Properties

        /// <summary>
        /// Gets or sets a list of the symbol IDs generated for this game instance.
        /// Note: For prize-based games, this list will contain prize tier indices instead of symbol IDs.
        /// </summary>
        public List<int> GeneratedSymbolIds { get; set; }

        /// <summary>
        /// Gets or sets the prize tier index won in this specific game, relative to the project's master PrizeTiers list.
        /// A value of -1 indicates no win for this particular game.
        /// </summary>
        public int PrizeTierIndex { get; set; }

        /// <summary>
        /// Gets or sets a flag indicating which game on the ticket is the winning one.
        /// (e.g., 1 for Game 1, 2 for Game 2, etc.). A value of 0 indicates a loser.
        /// This is primarily used for legacy file formats and final reports.
        /// </summary>
        public int WinningGameFlag { get; set; }

        /// <summary>
        /// Gets or sets the game number this play data belongs to, linking it back to a specific GameModule instance.
        /// </summary>
        public int GameNumber { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="GamePlayData"/> class.
        /// </summary>
        public GamePlayData()
        {
            // Initialise the list and set default "losing" values in the constructor.
            // This ensures the object is created in a clean, predictable state.
            GeneratedSymbolIds = new List<int>();
            PrizeTierIndex = -1; // Default to no win.
            WinningGameFlag = 0;
            GameNumber = 0;
        }

        #endregion
    }
}