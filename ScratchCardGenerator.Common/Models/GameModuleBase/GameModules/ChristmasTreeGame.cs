#region Usings

using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Christmas Tree Game

    /// <summary>
    /// Represents a themed "Find Winning Symbol" game with a fixed layout of 15 symbols arranged in a Christmas tree shape.
    /// </summary>
    /// <remarks>
    /// This class inherits from <see cref="FindSymbolGameBase"/> and provides a fixed, non-configurable number of symbols.
    /// All of its complex gameplay and file generation logic is inherited directly from the base class, making this
    /// concrete implementation extremely simple and focused only on defining the game's size.
    /// </remarks>
    public class ChristmasTreeGame : FindSymbolGameBase
    {
        #region Private Fields

        /// <summary>
        /// Defines the fixed layout of the game panel, representing rows with an increasing number of symbols
        /// to form a tree or pyramid shape. The sum of these values determines the total number of symbols.
        /// </summary>
        private readonly int[] _rowLayout = { 1, 2, 3, 4, 5 };

        #endregion

        #region Properties

        /// <summary>
        /// Overrides the abstract base property to provide the fixed total number of symbols for this game.
        /// The value is calculated by summing the integers in the private <see cref="_rowLayout"/> array.
        /// This property is read-only, as the size of the Christmas Tree game is not intended to be configurable.
        /// </summary>
        public override int NumberOfSymbols => _rowLayout.Sum(); // The Sum() method efficiently calculates the total (15).

        #endregion
    }

    #endregion
}