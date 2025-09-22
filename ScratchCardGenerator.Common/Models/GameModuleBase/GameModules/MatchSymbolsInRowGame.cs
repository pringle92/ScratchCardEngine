#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Match Symbols In Row Game

    /// <summary>
    /// Represents a game with multiple independent rows, where a win occurs if a single row
    /// contains the required number of matching symbols.
    /// </summary>
    /// <remarks>
    /// This class inherits from <see cref="RowGameModuleBase"/>, which provides the core algorithm for generating
    /// game data on a row-by-row basis. This concrete implementation is responsible only for supplying the
    /// abstract methods with symbol-specific data.
    /// </remarks>
    public class MatchSymbolsInRowGame : RowGameModuleBase
    {
        #region Private Fields

        /// <summary>
        /// A separate, static Random instance used exclusively for generating the CSV report data.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the number of symbols that appear in each row. This maps to the base class's
        /// <see cref="RowGameModuleBase.ItemsPerRow"/> property.
        /// </summary>
        public int SymbolsPerRow
        {
            get => base.ItemsPerRow;
            set => base.ItemsPerRow = value;
        }

        /// <summary>
        /// Gets or sets the number of identical symbols required within a single row to constitute a win.
        /// This maps to the base class's <see cref="RowGameModuleBase.ItemsToMatch"/> property.
        /// </summary>
        public int SymbolsToMatchInRow
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
        /// Provides the winning item for a winning panel by selecting a random symbol from the pool.
        /// </summary>
        /// <param name="winTier">The prize tier being won (not directly used in this implementation).</param>
        /// <param name="project">The project context.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        /// <returns>The integer ID of the randomly selected winning symbol.</returns>
        protected override int GetWinningItem(PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var symbolPool = GetItemPool(project);
            return symbolPool[random.Next(symbolPool.Count)];
        }

        /// <summary>
        /// Provides a pool of decoy symbols, excluding the chosen winning symbol.
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
        /// Provides the symbol associated with the highest-value prize.
        /// </summary>
        /// <param name="project">The project context.</param>
        /// <returns>The integer ID of the symbol linked to the highest prize, or -1 if not found.</returns>
        protected override int GetHighestValueItem(ScratchCardProject project)
        {
            var highestPrize = project.PrizeTiers
                .Where(p => !p.IsOnlinePrize && p.Value > 0)
                .OrderByDescending(p => p.Value)
                .FirstOrDefault();

            if (highestPrize == null) return -1;

            var symbol = project.AvailableSymbols.FirstOrDefault(s => s.Name == highestPrize.TextCode);
            return symbol?.Id ?? -1;
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// Headers are formatted to distinguish between rows (e.g., G1Sym1_1, G1Sym1_2 for Game 1, Row 1, Symbols 1 and 2).
        /// </summary>
        /// <returns>A list of strings representing the unique column headers for this game module.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int r = 1; r <= NumberOfRows; r++)
            {
                for (int s = 1; s <= SymbolsPerRow; s++)
                {
                    headers.Add($"G{GameNumber}Sym{r}_{s}");
                    headers.Add($"G{GameNumber}SymName{r}_{s}");
                    headers.Add($"G{GameNumber}S{r}_{s}");
                    headers.Add($"G{GameNumber}P{r}_{s}");
                    headers.Add($"G{GameNumber}T{r}_{s}");
                    headers.Add($"G{GameNumber}Status{r}_{s}");
                }
            }
            return headers;
        }

        /// <summary>
        /// Gets the data for this game as a list of strings for a single row in the final CSV report.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up names from IDs.</param>
        /// <returns>A list of strings representing all cell values for this game.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                bool isWinningGame = playData.PrizeTierIndex >= 0;
                PrizeTier winPrize = isWinningGame ? project.PrizeTiers[playData.PrizeTierIndex] : null;
                int winningRow = -1;

                // If the game is a winner, we must first identify which row contains the win.
                if (isWinningGame)
                {
                    for (int r = 0; r < NumberOfRows; r++)
                    {
                        var rowSymbols = playData.GeneratedSymbolIds.Skip(r * SymbolsPerRow).Take(SymbolsPerRow).ToList();
                        if (rowSymbols.GroupBy(s => s).Any(g => g.Count() >= SymbolsToMatchInRow))
                        {
                            winningRow = r;
                            break;
                        }
                    }
                }

                var decoyPrizes = project.PrizeTiers.Where(p => p.Value >= project.Settings.TicketSalePrice).ToList();
                for (int r = 0; r < NumberOfRows; r++)
                {
                    for (int s = 0; s < SymbolsPerRow; s++)
                    {
                        int index = r * SymbolsPerRow + s;
                        if (index < playData.GeneratedSymbolIds.Count)
                        {
                            int symbolId = playData.GeneratedSymbolIds[index];
                            string symbolName = project.AvailableSymbols.FirstOrDefault(sym => sym.Id == symbolId)?.DisplayText ?? "N/A";
                            rowData.Add(symbolId.ToString());
                            rowData.Add($"\"{symbolName}\"");

                            // If this is the designated winning row, display the actual prize information.
                            if (isWinningGame && r == winningRow)
                            {
                                string pence = (winPrize.Value > 0 && winPrize.Value < 100) ? ".00" : "";
                                rowData.Add($"\"{winPrize.DisplayText}\"");
                                rowData.Add($"\"{pence}\"");
                                rowData.Add($"\"{winPrize.TextCode}\"");
                                rowData.Add($"\"WIN\"");
                            }
                            else
                            {
                                // Otherwise, display a random decoy prize for verisimilitude.
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
                            // Pad with empty data if necessary.
                            rowData.AddRange(new[] { "0", "\"\"", "\"\"", "\"\"", "\"\"", "\"\"" });
                        }
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion
}