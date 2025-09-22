#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Online Bonus Game

    /// <summary>
    /// Represents a special game module for online bonus prizes, typically represented by a QR code on the physical card.
    /// </summary>
    /// <remarks>
    /// This module is unique in that it does not generate any playable symbols for the user to interact with.
    /// Instead, it acts as a designated container for any prize tier that is marked with the 'IsOnlinePrize' flag.
    /// If a ticket is set to award an online prize, this module will be chosen as the winner, and its primary
    /// role during file generation is to provide the base URL for the QR code.
    /// </remarks>
    public class OnlineBonusGame : GameModule
    {
        #region Private Fields

        /// <summary>
        /// The private backing field for the <see cref="Url"/> property.
        /// </summary>
        private string _url;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the base URL that will be encoded in the QR code.
        /// During the final file generation stage, a unique security code is appended to this URL
        /// to create the final, unique link for each ticket.
        /// </summary>
        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for the Online Bonus Game. Since this game has no symbols,
        /// its only task is to record the prize tier index if it's designated as the winning module.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared <see cref="Random"/> instance (not used in this implementation but required by the abstract class).</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardGenerator.Common.Models.ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };

            // If this module is designated as the winner for an online prize, we simply record which prize was won.
            // No symbols are generated as this game is not "played" in the traditional sense.
            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers for URL and QR Code data.</returns>
        public override List<string> GetCsvHeaders()
        {
            // Defines the columns for the base URL and the unique QR code that will be generated later.
            return new List<string> { $"URL", $"QRCode" };
        }

        /// <summary>
        /// Gets the data for this game as a list of strings for a single row in the final CSV report.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context.</param>
        /// <returns>A list of strings containing the base URL and a placeholder for the final unique code.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardGenerator.Common.Models.ScratchCardProject project)
        {
            // Note: The final, unique security code is appended to the URL in the FileGenerationService
            // during the 'Generate Combined Files' step. A generic placeholder is used here in the per-module data.
            return new List<string> { $"\"{Url ?? string.Empty}\"", "\"SAMPLE_CODE\"" };
        }

        #endregion
    }

    #endregion
}