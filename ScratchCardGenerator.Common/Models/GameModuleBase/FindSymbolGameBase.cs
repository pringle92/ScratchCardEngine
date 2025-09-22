#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Find Symbol Game Base

    /// <summary>
    /// Provides an abstract base class for game modules where the objective is to find a specific,
    /// pre-defined winning symbol within a set of other symbols.
    /// </summary>
    /// <remarks>
    /// This class contains the core logic for generating winning (contains the winning symbol) and losing
    /// (does not contain the winning symbol) game panels. Concrete subclasses are only required to
    /// implement the abstract <see cref="NumberOfSymbols"/> property to specify how many total symbols
    /// their game variant should contain. This allows the shared generation and file-writing logic
    /// to adapt to different game sizes.
    /// </remarks>
    public abstract class FindSymbolGameBase : GameModule
    {
        #region Private Fields

        /// <summary>
        /// A separate, static Random instance used exclusively for generating the CSV report data.
        /// Using a distinct instance ensures that the selection of random decoy prizes for the CSV report
        /// does not interfere with the main gameplay generation's random sequence, which is critical for
        /// security and the reproducibility of ticket data.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the ID of the symbol that constitutes a win if found within the generated panel.
        /// This ID corresponds to a <see cref="Symbol"/> in the project's <c>AvailableSymbols</c> collection.
        /// </summary>
        public int WinningSymbolId { get; set; }

        /// <summary>
        /// When implemented in a derived class, gets the total number of symbols to be displayed in the game panel.
        /// This allows the base class logic to be reused for games of different sizes.
        /// </summary>
        public abstract int NumberOfSymbols { get; }

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a "Find Winning Symbol" style game instance.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared <see cref="Random"/> instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var symbols = new List<int>();

            // Gracefully handle cases where no symbols are defined in the project.
            if (project.AvailableSymbols == null || !project.AvailableSymbols.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            // Create a pool of "losing" or "decoy" symbols by excluding the designated winning symbol.
            var losingSymbolPool = project.AvailableSymbols.Where(s => s.Id != WinningSymbolId).Select(s => s.Id).ToList();

            // If there are no losing symbols to act as decoys, a winning game cannot be unambiguously generated.
            // In this edge case, we must downgrade the game to a losing one.
            if (!losingSymbolPool.Any() && isWinningGame)
            {
                isWinningGame = false;
            }

            if (isWinningGame)
            {
                // --- Winning Game Logic ---
                // 1. Add the required winning symbol to the list.
                symbols.Add(WinningSymbolId);

                // 2. Fill the remaining slots with random symbols from the losing pool.
                for (int i = 0; i < NumberOfSymbols - 1; i++)
                {
                    symbols.Add(losingSymbolPool[random.Next(losingSymbolPool.Count)]);
                }

                // 3. Shuffle the list to randomise the position of the winning symbol.
                Shuffle(symbols, random);
            }
            else
            {
                // --- Losing Game Logic ---
                // A losing game is simply a list of symbols drawn exclusively from the losing pool.
                for (int i = 0; i < NumberOfSymbols; i++)
                {
                    if (losingSymbolPool.Any())
                    {
                        symbols.Add(losingSymbolPool[random.Next(losingSymbolPool.Count)]);
                    }
                }
            }

            playData.GeneratedSymbolIds.AddRange(symbols);

            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Shuffles an integer list in-place using the Fisher-Yates algorithm.
        /// </summary>
        /// <param name="list">The list to shuffle.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        private void Shuffle(List<int> list, Random random)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report. The number of
        /// columns generated is driven by the <see cref="NumberOfSymbols"/> property.
        /// </summary>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int i = 1; i <= NumberOfSymbols; i++)
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
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);

            if (playData != null)
            {
                bool isWinningGame = playData.PrizeTierIndex >= 0;
                PrizeTier winPrize = isWinningGame ? project.PrizeTiers[playData.PrizeTierIndex] : null;
                var decoyPrizes = project.PrizeTiers.Where(p => p.Value >= project.Settings.TicketSalePrice).ToList();

                for (int i = 0; i < NumberOfSymbols; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        int symbolId = playData.GeneratedSymbolIds[i];
                        string symbolName = project.AvailableSymbols.FirstOrDefault(s => s.Id == symbolId)?.DisplayText ?? "N/A";

                        rowData.Add(symbolId.ToString());
                        rowData.Add($"\"{symbolName}\"");

                        // If this game is a winner and the current symbol is THE winning symbol, add the actual prize data.
                        if (isWinningGame && symbolId == this.WinningSymbolId)
                        {
                            string pence = (winPrize.Value > 0 && winPrize.Value < 100) ? ".00" : "";
                            rowData.Add($"\"{winPrize.DisplayText}\"");
                            rowData.Add($"\"{pence}\"");
                            rowData.Add($"\"{winPrize.TextCode}\"");
                            rowData.Add($"\"WIN\"");
                        }
                        else
                        {
                            // Otherwise, for completeness in the report, add data from a randomly selected decoy prize.
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
                                // Fallback for when no decoy prizes are configured.
                                rowData.AddRange(new[] { "\"\"", "\"\"", "\"\"", "\"NO WIN\"" });
                            }
                        }
                    }
                    else
                    {
                        // Pad with empty data if necessary (should not happen in normal operation).
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