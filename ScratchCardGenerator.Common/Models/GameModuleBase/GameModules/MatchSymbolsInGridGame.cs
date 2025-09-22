#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Match Symbols In Grid Game

    /// <summary>
    /// Represents a game where the player must find a certain number of matching symbols within a grid to win.
    /// This class inherits from <see cref="GridGameModuleBase"/> and provides the concrete implementation for the
    /// abstract "hook" methods, supplying symbol-specific data to the base class's generation algorithm.
    /// </summary>
    public class MatchSymbolsInGridGame : GridGameModuleBase
    {
        #region Private Fields

        /// <summary>
        /// A separate, static Random instance used exclusively for generating the CSV report data.
        /// This ensures that the selection of random decoy prizes for the CSV does not interfere with the
        /// main, security-sensitive gameplay generation sequence.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the number of identical symbols required to constitute a win.
        /// This property provides a user-friendly name in the UI's property editor and maps directly 
        /// to the base class's <see cref="GridGameModuleBase.ItemsToMatch"/> property.
        /// </summary>
        public int SymbolsToMatch
        {
            get => base.ItemsToMatch;
            set => base.ItemsToMatch = value;
        }

        #endregion

        #region Abstract Method Implementations

        /// <summary>
        /// Provides the complete pool of available symbol IDs from the project.
        /// </summary>
        /// <param name="project">The project context containing the list of available symbols.</param>
        /// <returns>A list of integers representing the IDs of all available symbols.</returns>
        protected override List<int> GetItemPool(ScratchCardProject project)
        {
            return project.AvailableSymbols?.Select(s => s.Id).ToList() ?? new List<int>();
        }

        /// <summary>
        /// Provides the winning item for a winning panel. For this game, it selects a random symbol
        /// from the entire pool to act as the winning symbol for this specific ticket.
        /// </summary>
        /// <param name="winTier">The prize tier being won (not directly used in this implementation but required by the signature).</param>
        /// <param name="project">The project context.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        /// <returns>The integer ID of the randomly selected winning symbol.</returns>
        protected override int GetWinningItem(PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var symbolPool = GetItemPool(project);
            // For a standard symbol-matching game, any symbol can be the winner.
            // We randomly select one from the pool for this ticket instance.
            return symbolPool[random.Next(symbolPool.Count)];
        }

        /// <summary>
        /// Provides a pool of decoy symbols, which is simply all available symbols minus the one chosen to be the winner.
        /// </summary>
        /// <param name="winningItem">The ID of the winning symbol to exclude.</param>
        /// <param name="fullPool">The full pool of all symbol IDs.</param>
        /// <param name="project">The project context (not used in this implementation).</param>
        /// <returns>A list of symbol IDs that can be used as decoys.</returns>
        protected override List<int> GetDecoyItemPool(int winningItem, List<int> fullPool, ScratchCardProject project)
        {
            return fullPool.Where(s => s != winningItem).ToList();
        }

        /// <summary>
        /// Provides the symbol associated with the highest-value prize. This is determined by a convention where
        /// the prize's 'TextCode' (e.g., "ONETHOU") matches the symbol's internal 'Name'.
        /// </summary>
        /// <param name="project">The project context.</param>
        /// <returns>The integer ID of the symbol linked to the highest prize, or -1 if not found.</returns>
        protected override int GetHighestValueItem(ScratchCardProject project)
        {
            // Find the highest-value, non-online prize tier.
            var highestPrize = project.PrizeTiers
                .Where(p => !p.IsOnlinePrize && p.Value > 0)
                .OrderByDescending(p => p.Value)
                .FirstOrDefault();

            if (highestPrize == null) return -1;

            // Find the symbol whose internal name matches the prize's text code.
            var symbol = project.AvailableSymbols.FirstOrDefault(s => s.Name == highestPrize.TextCode);
            return symbol?.Id ?? -1;
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers for each position in the grid.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int i = 1; i <= Rows * Columns; i++)
            {
                headers.Add($"G{GameNumber}Sym{i}");
                headers.Add($"G{GameNumber}SymName{i}");
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
        /// <param name="ticket">The ticket containing the generated play data for this game.</param>
        /// <param name="project">The project context, used for looking up human-readable names from IDs.</param>
        /// <returns>A list of strings representing all cell values for this game on a single ticket row.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);

            if (playData != null)
            {
                bool isWinningGame = playData.PrizeTierIndex >= 0;
                PrizeTier winPrize = isWinningGame ? project.PrizeTiers[playData.PrizeTierIndex] : null;

                // Determine the winning symbol ID if this game was a winner.
                int winningSymbolId = -1;
                if (isWinningGame)
                {
                    var winningGroup = playData.GeneratedSymbolIds.GroupBy(s => s).FirstOrDefault(g => g.Count() >= SymbolsToMatch);
                    if (winningGroup != null) winningSymbolId = winningGroup.Key;
                }

                var decoyPrizes = project.PrizeTiers.Where(p => p.Value >= project.Settings.TicketSalePrice).ToList();
                int gridSize = Rows * Columns;

                for (int i = 0; i < gridSize; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        int symbolId = playData.GeneratedSymbolIds[i];
                        string symbolName = project.AvailableSymbols.FirstOrDefault(s => s.Id == symbolId)?.DisplayText ?? "N/A";

                        rowData.Add(symbolId.ToString());
                        rowData.Add($"\"{symbolName}\"");

                        // If this is a winning game and the current symbol is one of the winning ones, add the actual prize data.
                        if (isWinningGame && symbolId == winningSymbolId)
                        {
                            string pence = (winPrize.Value > 0 && winPrize.Value < 100) ? ".00" : "";
                            rowData.Add($"\"{winPrize.DisplayText}\"");
                            rowData.Add($"\"{pence}\"");
                            rowData.Add($"\"{winPrize.TextCode}\"");
                            rowData.Add($"\"WIN\"");
                        }
                        else
                        {
                            // Otherwise, add data from a randomly selected decoy prize.
                            if (decoyPrizes.Any())
                            {
                                var randomPrize = decoyPrizes[_csvRandom.Next(decoyPrizes.Count)];
                                string pence = (randomPrize.Value > 0 && randomPrize.Value < 100) ? ".00" : "";
                                rowData.Add($"\"{randomPrize.DisplayText}\"");
                                rowData.Add($"\"{pence}\"");
                                rowData.Add($"\"{randomPrize.TextCode}\"");
                                rowData.Add("\"NO WIN\"");
                            }
                            else
                            {
                                rowData.AddRange(new[] { "\"\"", "\"\"", "\"\"", "\"NO WIN\"" });
                            }
                        }
                    }
                    else
                    {
                        // Pad with empty data if the grid is larger than the number of generated symbols.
                        rowData.AddRange(new[] { "0", "\"\"", "\"\"", "\"\"", "\"\"", "\"\"" });
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion
}