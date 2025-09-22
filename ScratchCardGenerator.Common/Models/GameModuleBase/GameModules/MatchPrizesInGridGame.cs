#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Match Prizes In Grid Game

    /// <summary>
    /// Represents a game where the player must find a certain number of matching prize values within a grid to win that prize.
    /// This class inherits from <see cref="GridGameModuleBase"/> and provides the concrete implementation for the
    /// abstract "hook" methods, supplying prize-specific data to the base class's generation algorithm.
    /// </summary>
    public class MatchPrizesInGridGame : GridGameModuleBase
    {
        #region Properties

        /// <summary>
        /// Gets or sets the number of identical prize values required to constitute a win.
        /// This property provides a user-friendly name and maps directly to the base class's 
        /// <see cref="GridGameModuleBase.ItemsToMatch"/> property.
        /// </summary>
        public int PrizesToMatch
        {
            get => base.ItemsToMatch;
            set => base.ItemsToMatch = value;
        }

        #endregion

        #region Abstract Method Implementations

        /// <summary>
        /// Provides the pool of all valid, winnable, non-online prize indices from the project.
        /// The "items" in this game are the integer indices of the prizes in the project's master prize list.
        /// </summary>
        /// <param name="project">The project context containing the list of prize tiers.</param>
        /// <returns>A list of integers representing the indices of all valid prize tiers.</returns>
        protected override List<int> GetItemPool(ScratchCardGenerator.Common.Models.ScratchCardProject project)
        {
            return project.PrizeTiers?
                .Select((p, i) => new { Prize = p, Index = i })
                .Where(x => x.Prize.Value > 0 && !x.Prize.IsOnlinePrize)
                .Select(x => x.Index)
                .ToList() ?? new List<int>();
        }

        /// <summary>
        /// Provides the winning item for a winning panel. For this game, the winning item is simply the
        /// index of the prize tier that is being awarded on this ticket.
        /// </summary>
        /// <param name="winTier">The prize tier being won.</param>
        /// <param name="project">The project context.</param>
        /// <param name="random">The shared <see cref="Random"/> instance (not used in this implementation).</param>
        /// <returns>The integer index of the winning prize tier in the project's master list.</returns>
        protected override int GetWinningItem(PrizeTier winTier, ScratchCardGenerator.Common.Models.ScratchCardProject project, Random random)
        {
            return project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
        }

        /// <summary>
        /// Provides a pool of decoy prize indices. This is defined as all valid prize indices whose prize value
        /// is different from the winning prize's value. This prevents accidental wins in the decoy area.
        /// </summary>
        /// <param name="winningItem">The index of the winning prize tier to exclude.</param>
        /// <param name="fullPool">The full pool of all valid prize indices.</param>
        /// <param name="project">The project context, used to look up prize values from their indices.</param>
        /// <returns>A list of prize indices that can be used as decoys.</returns>
        protected override List<int> GetDecoyItemPool(int winningItem, List<int> fullPool, ScratchCardGenerator.Common.Models.ScratchCardProject project)
        {
            PrizeTier winningPrize = project.PrizeTiers[winningItem];
            return fullPool.Where(pIndex => project.PrizeTiers[pIndex].Value != winningPrize.Value).ToList();
        }

        /// <summary>
        /// Provides the index of the highest-value prize. This is used by the base class to avoid
        /// creating a near-miss of the jackpot prize on a losing ticket.
        /// </summary>
        /// <param name="project">The project context.</param>
        /// <returns>The integer index of the highest-value prize, or -1 if not found.</returns>
        protected override int GetHighestValueItem(ScratchCardGenerator.Common.Models.ScratchCardProject project)
        {
            var highestPrize = project.PrizeTiers.Where(p => !p.IsOnlinePrize).OrderByDescending(p => p.Value).FirstOrDefault();
            return highestPrize != null ? project.PrizeTiers.ToList().IndexOf(highestPrize) : -1;
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// The structure is simpler than the symbol-based game as it doesn't need columns for symbol names.
        /// </summary>
        /// <returns>A list of strings representing the column headers.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int i = 1; i <= Rows * Columns; i++)
            {
                headers.Add($"G{GameNumber}S{i}");         // Prize Text (e.g., "£50")
                headers.Add($"G{GameNumber}P{i}");         // Pence value (legacy)
                headers.Add($"G{GameNumber}T{i}");         // Prize Text Code (e.g., "FIFTY")
                headers.Add($"G{GameNumber}Status{i}");    // "WIN" or "NO WIN"
            }
            return headers;
        }

        /// <summary>
        /// Gets the data for this game as a list of strings for a single row in the final CSV report.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up prize details from their indices.</param>
        /// <returns>A list of strings representing all cell values for this game on a single ticket row.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardGenerator.Common.Models.ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                bool isWinningGame = playData.PrizeTierIndex >= 0;
                for (int i = 0; i < Rows * Columns; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        // The "SymbolId" in this context is actually the index into the project's PrizeTiers collection.
                        int prizeIndex = playData.GeneratedSymbolIds[i];
                        PrizeTier prize = project.PrizeTiers[prizeIndex];
                        string pence = (prize.Value > 0 && prize.Value < 100) ? ".00" : "";

                        rowData.Add($"\"{prize.DisplayText}\"");
                        rowData.Add($"\"{pence}\"");
                        rowData.Add($"\"{prize.TextCode}\"");

                        // Determine if this specific prize is part of the winning set.
                        bool isWinningPrize = isWinningGame && playData.GeneratedSymbolIds.Count(p => p == prizeIndex) >= PrizesToMatch;
                        rowData.Add(isWinningPrize ? "\"WIN\"" : "\"NO WIN\"");
                    }
                    else
                    {
                        // Pad with empty data if necessary.
                        rowData.AddRange(new[] { "\"\"", "\"\"", "\"\"", "\"\"" });
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion
}