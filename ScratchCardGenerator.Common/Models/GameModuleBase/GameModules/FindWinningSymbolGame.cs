#region Usings

using ScratchCardGenerator.Common.Models;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Find Winning Symbol Game

    /// <summary>
    /// Represents a game where the player reveals a set of symbols and wins if a specific, pre-defined "Winning Symbol" is present.
    /// This class inherits from the <see cref="FindSymbolGameBase"/>, specifying the number of symbols to generate.
    /// </summary>
    /// <remarks>
    /// As a concrete implementation of <c>FindSymbolGameBase</c>, this class is now extremely lightweight. 
    /// It inherits all of its gameplay generation and file-writing logic, and its primary purpose is to provide a concrete,
    /// user-configurable value for the abstract <see cref="FindSymbolGameBase.NumberOfSymbols"/> property.
    /// </remarks>
    public class FindWinningSymbolGame : FindSymbolGameBase
    {
        #region Private Fields

        /// <summary>
        /// The private backing field for the configurable number of symbols property.
        /// </summary>
        private int _numberOfSymbols = 1;

        #endregion

        #region Properties

        /// <summary>
        /// Overrides the abstract base property to provide the total number of symbols for this game variant.
        /// The implementation simply returns the value from the private backing field.
        /// </summary>
        public override int NumberOfSymbols
        {
            get => _numberOfSymbols;
        }

        /// <summary>
        /// Gets or sets the total number of symbols to be displayed.
        /// This property is provided with a public setter specifically for the UI's property editor to bind to.
        /// It updates the private field that the overridden <see cref="NumberOfSymbols"/> property reads from.
        /// </summary>
        /// <remarks>
        /// This "Setter" property is a common pattern when a class needs to provide a value for a read-only
        /// abstract property from its base class, but still allow that value to be configured.
        /// </remarks>
        public int NumberOfSymbolsSetter
        {
            get => _numberOfSymbols;
            set
            {
                // We update the private field and then notify the UI that the public, read-only property may have changed.
                _numberOfSymbols = value;
                OnPropertyChanged(nameof(NumberOfSymbols));
            }
        }

        #endregion
    }

    #endregion
}