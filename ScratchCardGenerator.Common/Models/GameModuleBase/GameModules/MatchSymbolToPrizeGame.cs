#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Match Symbol To Prize Game

    /// <summary>
    /// Represents a special game where finding a specific "winning symbol" awards a prize that is
    /// directly linked to that symbol by a shared name convention.
    /// </summary>
    /// <remarks>
    /// This module is designed for "find a number, win that amount" style games. It operates on the dedicated
    /// <c>NumericSymbols</c> collection from the project. When the main <see cref="Services.FileGenerationService"/>
    /// assigns a prize (e.g., a prize with the <c>TextCode</c> "TEN") to this game, this module is responsible for
    /// generating a winning panel that correctly includes the corresponding symbol (the symbol with the <c>Name</c> "TEN").
    /// A critical feature is its "game-aware" logic, which prevents it from using the winning symbols of
    /// other <c>MatchSymbolToPrizeGame</c> instances on the same card as decoys, avoiding conflicts.
    /// </remarks>
    public class MatchSymbolToPrizeGame : GameModule
    {
        #region Properties

        /// <summary>
        /// Gets or sets the total number of symbols to be displayed in the game panel.
        /// </summary>
        public int NumberOfSymbols { get; set; } = 6;

        /// <summary>
        /// Gets or sets the ID of the symbol that this game is primarily associated with in the UI.
        /// This is used by the <see cref="Services.FileGenerationService"/> to identify which game instance
        /// is linked to which prize via the symbol's name and prize's TextCode.
        /// </summary>
        public int WinningSymbolId { get; set; }

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Match Symbol to Prize game instance.
        /// </summary>
        /// <remarks>
        /// This module does not determine its own prize. Instead, if the <paramref name="isWinningGame"/> parameter is true,
        /// it respects the <paramref name="winTier"/> passed in by the central generator. It then uses the <c>TextCode</c>
        /// of that <paramref name="winTier"/> to find the correct symbol that must be displayed as the winner, ensuring the
        /// gameplay correctly reflects the assigned prize.
        /// </remarks>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing symbols and prizes.</param>
        /// <param name="random">A shared <see cref="Random"/> instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardGenerator.Common.Models.ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var symbols = new List<int>();

            // This game requires the NumericSymbols collection to be populated. If not, exit gracefully.
            if (project.NumericSymbols == null || !project.NumericSymbols.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            // --- Game-Aware Decoy Selection ---
            // To prevent conflicts, we must exclude the winning symbols from *other* "Match Symbol to Prize" games
            // from being used as decoys in this game. This is critical when multiple instances of this game are on one card.
            var excludedSymbolIds = project.Layout.GameModules
                .OfType<MatchSymbolToPrizeGame>()
                .Where(g => g.GameNumber != this.GameNumber) // Exclude this instance of the game.
                .Select(g => g.WinningSymbolId)
                .ToList();

            if (isWinningGame)
            {
                // --- Winning Game Logic ---
                // 1. Determine the required winning symbol based on the prize tier passed into this method.
                var requiredWinningSymbol = project.NumericSymbols.FirstOrDefault(s => s.Name == winTier.TextCode);

                // 2. Safety check: If no symbol is linked to the prize we're supposed to award, downgrade to a losing game.
                //    This scenario should not happen in normal operation, as the FileGenerationService would not have selected this module.
                if (requiredWinningSymbol == null)
                {
                    isWinningGame = false;
                }
                else
                {
                    int actualWinningSymbolId = requiredWinningSymbol.Id;

                    // 3. Add the required winning symbol to the panel.
                    symbols.Add(actualWinningSymbolId);

                    // 4. Create a pool of decoy symbols, excluding our winning symbol AND any from other similar games.
                    var losingSymbolPool = project.NumericSymbols
                        .Where(s => s.Id != actualWinningSymbolId && !excludedSymbolIds.Contains(s.Id))
                        .Select(s => s.Id)
                        .ToList();

                    // 5. Fill the rest of the panel with decoy symbols.
                    for (int i = 0; i < NumberOfSymbols - 1; i++)
                    {
                        if (losingSymbolPool.Any())
                        {
                            symbols.Add(losingSymbolPool[random.Next(losingSymbolPool.Count)]);
                        }
                    }
                    // 6. Shuffle the panel to randomise the winning symbol's position.
                    Shuffle(symbols, random);
                }
            }

            if (!isWinningGame)
            {
                // --- Losing Game Logic ---
                // A losing panel consists only of symbols from a safe, non-winning decoy pool.
                // This pool must exclude ALL possible winning symbols from ANY MatchSymbolToPrize game instance on the card.
                var allConfiguredWinningSymbolIds = project.Layout.GameModules.OfType<MatchSymbolToPrizeGame>().Select(g => g.WinningSymbolId).ToList();
                var losingSymbolPool = project.NumericSymbols
                    .Where(s => !allConfiguredWinningSymbolIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToList();

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
                // Record the prize tier index using the 'winTier' parameter that was passed in, ensuring consistency.
                // Using the prize tier's unique ID is the most robust way to find the correct index.
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Id == winTier.Id);
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
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers for each symbol position.</returns>
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
        /// This implementation is unique as it looks up the prize associated with each individual symbol to populate the row,
        /// reflecting the "find a number, win that amount" gameplay.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up symbol and prize details.</param>
        /// <returns>A list of strings representing all cell values for this game on a single ticket row.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardGenerator.Common.Models.ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                // If the game was a winner, we need to identify which symbol ID was the winning one for this ticket.
                int actualWinningSymbolId = -1;
                if (playData.PrizeTierIndex >= 0)
                {
                    var winningPrize = project.PrizeTiers[playData.PrizeTierIndex];
                    // We find the symbol whose name matches the winning prize's code.
                    actualWinningSymbolId = project.NumericSymbols.FirstOrDefault(s => s.Name == winningPrize.TextCode)?.Id ?? -1;
                }

                for (int i = 0; i < NumberOfSymbols; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        int symbolId = playData.GeneratedSymbolIds[i];
                        var symbol = project.NumericSymbols.FirstOrDefault(s => s.Id == symbolId);

                        // For each symbol on the panel, we find the prize tier that corresponds to its name.
                        var prize = project.PrizeTiers.FirstOrDefault(p => !p.IsOnlinePrize && p.TextCode == symbol?.Name);

                        rowData.Add(symbolId.ToString());
                        rowData.Add($"\"{symbol?.DisplayText ?? "N/A"}\"");

                        if (prize != null)
                        {
                            // If a corresponding prize exists, we write its details.
                            string pence = (prize.Value > 0 && prize.Value < 100) ? ".00" : "";
                            rowData.Add($"\"{prize.DisplayText}\"");
                            rowData.Add($"\"{pence}\"");
                            rowData.Add($"\"{prize.TextCode}\"");
                        }
                        else
                        {
                            // If a symbol doesn't correspond to a prize, it's a decoy, so we write empty prize info.
                            rowData.AddRange(new[] { "\"\"", "\"\"", "\"\"" });
                        }

                        bool isWinningGame = playData.PrizeTierIndex >= 0;
                        // The status is "WIN" only if this symbol is the designated winning symbol on a winning ticket.
                        rowData.Add(isWinningGame && symbolId == actualWinningSymbolId ? "\"WIN\"" : "\"NO WIN\"");
                    }
                    else
                    {
                        // Pad with empty data if necessary.
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