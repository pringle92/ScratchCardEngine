namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Represents the raw, 16-byte data structure of a single ticket record as it might be read from
    /// or written to a legacy binary file format (e.g., intermediate .fil files).
    /// This class acts as a direct mapping to that fixed-size binary structure and is likely used
    /// during a file combination or conversion process.
    /// </summary>
    public class RawTicketRecord
    {
        #region Properties

        /// <summary>
        /// Gets or sets the 9 bytes representing the symbols for the first game area in the legacy format.
        /// </summary>
        public byte[] Game1Symbols { get; set; } = new byte[9];

        /// <summary>
        /// Gets or sets the 3 dummy bytes for Game 1 prizes, as required by the original file format.
        /// </summary>
        public byte[] Game1Prizes { get; set; } = new byte[3];

        /// <summary>
        /// Gets or sets the single byte representing the second game's symbol.
        /// </summary>
        public byte Game2Symbol { get; set; }

        /// <summary>
        /// Gets or sets the single byte representing the second game's prize index.
        /// </summary>
        public byte Game2Prize { get; set; }

        /// <summary>
        /// Gets or sets the single byte representing the overall winning prize index of the ticket.
        /// </summary>
        public byte WinPrizeIndex { get; set; }

        /// <summary>
        /// Gets or sets the single byte flag indicating which game is the winning one (e.g., 1 for Game 1, 2 for Game 2).
        /// A value of 0 indicates a losing ticket.
        /// </summary>
        public byte WinGameFlag { get; set; }

        #endregion
    }
}