#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Match Prizes In Row Game

    /// <summary>
    /// Represents a game with multiple independent rows, where a win occurs if a single row
    /// contains the required number of matching prize values.
    /// </summary>
    /// <remarks>
    /// This class inherits from <see cref="RowGameModuleBase"/> and is responsible for providing prize-specific data
    /// (in the form of prize indices) to the base class's row-generation algorithm.
    /// </remarks>
    public class MatchPrizesInRowGame : RowGameModuleBase
    {
        #region Properties

        /// <summary>
        /// Gets or sets the number of prizes that appear in each row. This maps to the base class's
        /// <see cref="RowGameModuleBase.ItemsPerRow"/> property.
        /// </summary>
        public int PrizesPerRow
        {
            get => base.ItemsPerRow;
            set => base.ItemsPerRow = value;
        }

        /// <summary>
        /// Gets or sets the number of identical prizes required within a single row to constitute a win.
        /// This maps to the base class's <see cref="RowGameModuleBase.ItemsToMatch"/> property.
        /// </summary>
        public int PrizesToMatchInRow
        {
            get => base.ItemsToMatch;
            set => base.ItemsToMatch = value;
        }

        #endregion

        #region Abstract Method Implementations

        /// <summary>
        /// Provides the pool of all valid, winnable, non-online prize indices from the project.
        /// </summary>
        /// <param name="project">The project context containing the list of prize tiers.</param>
        /// <returns>A list of integers representing the indices of all valid prize tiers.</returns>
        protected override List<int> GetItemPool(ScratchCardProject project)
        {
            return project.PrizeTiers?
                .Select((p, i) => new { Prize = p, Index = i })
                .Where(x => x.Prize.Value > 0 && !x.Prize.IsOnlinePrize)
                .Select(x => x.Index)
                .ToList() ?? new List<int>();
        }

        /// <summary>
        /// Provides the winning item, which is the index of the prize tier being awarded.
        /// </summary>
        /// <param name="winTier">The prize tier being won.</param>
        /// <param name="project">The project context.</param>
        /// <param name="random">The shared <see cref="Random"/> instance (not used in this implementation).</param>
        /// <returns>The integer index of the winning prize tier.</returns>
        protected override int GetWinningItem(PrizeTier winTier, ScratchCardProject project, Random random)
        {
            return project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
        }

        /// <summary>
        /// Provides a pool of decoy prize indices, excluding any prize with the same value as the winning prize.
        /// </summary>
        /// <param name="winningItem">The index of the winning prize tier to exclude.</param>
        /// <param name="fullPool">The full pool of all valid prize indices.</param>
        /// <param name="project">The project context, used to look up prize values from their indices.</param>
        /// <returns>A list of prize indices that can be used as decoys.</returns>
        protected override List<int> GetDecoyItemPool(int winningItem, List<int> fullPool, ScratchCardProject project)
        {
            PrizeTier winningPrize = project.PrizeTiers[winningItem];
            return fullPool.Where(pIndex => project.PrizeTiers[pIndex].Value != winningPrize.Value).ToList();
        }

        /// <summary>
        /// Provides the index of the highest-value prize.
        /// </summary>
        /// <param name="project">The project context.</param>
        /// <returns>The integer index of the highest-value prize, or -1 if not found.</returns>
        protected override int GetHighestValueItem(ScratchCardProject project)
        {
            var highestPrize = project.PrizeTiers.Where(p => !p.IsOnlinePrize).OrderByDescending(p => p.Value).FirstOrDefault();
            return highestPrize != null ? project.PrizeTiers.ToList().IndexOf(highestPrize) : -1;
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            int totalPrizes = NumberOfRows * PrizesPerRow;
            for (int i = 1; i <= totalPrizes; i++)
            {
                headers.Add($"G{GameNumber}S{i}");
                headers.Add($"G{GameNumber}P{i}");
                headers.Add($"G{GameNumber}T{i}");
                headers.Add($"G{GameNumber}Status{i}");
            }
            return headers;
        }

        /// <summary>
        /// Gets the data for this game as a list of strings for a single row in the final CSV report.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up prize details.</param>
        /// <returns>A list of strings representing all cell values for this game.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                bool isWinningTicket = playData.PrizeTierIndex >= 0;
                int winningRow = -1;

                // If the game is a winner, identify which row won.
                if (isWinningTicket)
                {
                    for (int r = 0; r < NumberOfRows; r++)
                    {
                        var rowItems = playData.GeneratedSymbolIds.Skip(r * PrizesPerRow).Take(PrizesPerRow).ToList();
                        if (rowItems.GroupBy(s => s).Any(g => g.Count() >= PrizesToMatchInRow))
                        {
                            winningRow = r;
                            break;
                        }
                    }
                }

                int totalPrizes = NumberOfRows * PrizesPerRow;
                for (int i = 0; i < totalPrizes; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        int prizeIndex = playData.GeneratedSymbolIds[i];
                        PrizeTier prize = project.PrizeTiers[prizeIndex];
                        string pence = (prize.Value > 0 && prize.Value < 100) ? ".00" : "";

                        rowData.Add($"\"{prize.DisplayText}\"");
                        rowData.Add($"\"{pence}\"");
                        rowData.Add($"\"{prize.TextCode}\"");

                        // Mark the status as "WIN" only for items in the designated winning row.
                        int currentRow = i / PrizesPerRow;
                        rowData.Add(isWinningTicket && currentRow == winningRow ? "\"WIN\"" : "\"NO WIN\"");
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