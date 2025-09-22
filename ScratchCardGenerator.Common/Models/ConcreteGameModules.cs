#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using ScratchCardGenerator.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Match Symbols In Grid Game

    /// <summary>
    /// Represents a game where the player must find a certain number of matching symbols within a grid to win.
    /// This is a concrete implementation of the abstract GameModule.
    /// </summary>
    public class MatchSymbolsInGridGame : GameModule
    {
        #region Private Fields

        /// <summary>
        /// A separate Random instance used specifically for CSV generation.
        /// This ensures that the selection of random decoy prizes for the CSV report
        /// does not interfere with the main gameplay generation's random sequence,
        /// which is critical for reproducibility and security.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the number of rows in the game grid.
        /// </summary>
        public int Rows { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of columns in the game grid.
        /// </summary>
        public int Columns { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of identical symbols required to constitute a win.
        /// </summary>
        public int SymbolsToMatch { get; set; } = 3;

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Match Symbols in Grid game instance on a single ticket.
        /// This method contains the core logic for constructing both winning and losing panels according to the game's rules.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            int gridSize = Rows * Columns;
            var symbols = new int[gridSize];

            // Gracefully handle cases where no symbols are defined in the project to prevent exceptions.
            if (project.AvailableSymbols == null || !project.AvailableSymbols.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            // Create a pool of all available symbol IDs to draw from.
            var symbolPool = project.AvailableSymbols.Select(s => s.Id).ToList();

            if (isWinningGame)
            {
                // --- Winning Game Logic ---

                // 1. Select a random symbol from the pool to be the winning symbol.
                int winningSymbolId = symbolPool[random.Next(symbolPool.Count)];
                var decoySymbolPool = symbolPool.Where(s => s != winningSymbolId).ToList();

                // If there are no other symbols to act as decoys, a win is impossible to generate.
                // In this edge case, we downgrade the game to be a losing one.
                if (!decoySymbolPool.Any())
                {
                    isWinningGame = false;
                }
                else
                {
                    // 2. Place the required number of winning symbols into the grid.
                    for (int i = 0; i < SymbolsToMatch; i++)
                    {
                        symbols[i] = winningSymbolId;
                    }

                    // 3. Fill the rest of the grid with decoy symbols, ensuring no accidental wins are created by using the "No Near Misses" helper.
                    var decoyGridPart = new int[gridSize - SymbolsToMatch];
                    FillWithNoNearMisses(decoyGridPart, decoySymbolPool, random);

                    // 4. Copy the generated decoy symbols into the main grid array.
                    Array.Copy(decoyGridPart, 0, symbols, SymbolsToMatch, decoyGridPart.Length);

                    // 5. Shuffle the entire grid to randomise the positions of the winning and decoy symbols.
                    Shuffle(symbols, random);
                }
            }

            if (!isWinningGame)
            {
                // --- Losing Game Logic ---
                // The rules for generating a losing panel depend on the overall state of the ticket.

                if (ticket.WinPrize.Value > 0)
                {
                    // Rule: If the ticket is already a winner in another game, this losing panel must have no near misses
                    // (i.e., all symbols must be unique) to avoid confusing the player.
                    FillWithNoNearMisses(symbols, symbolPool, random);
                }
                else
                {
                    // Rule: This is a completely losing ticket, so we can generate a more engaging panel.
                    if (SymbolsToMatch <= 2)
                    {
                        // For simpler games (e.g., Match 2), a losing panel MUST have all unique symbols to be unambiguous.
                        FillWithNoNearMisses(symbols, symbolPool, random);
                    }
                    else // For "Match 3" or higher, near misses are allowed to make the game more exciting.
                    {
                        // As a special rule, avoid creating a near-miss of the game's highest value prize on a losing ticket.
                        // This prevents giving the player false hope for the jackpot.
                        var highestValueSymbol = GetHighestValueSymbol(project);
                        var filteredSymbolPool = highestValueSymbol == -1 ? symbolPool : symbolPool.Where(s => s != highestValueSymbol).ToList();
                        FillWithNearMisses(symbols, filteredSymbolPool.Any() ? filteredSymbolPool : symbolPool, SymbolsToMatch, random);
                    }
                }
            }

            // Add the final array of generated symbols to the play data.
            playData.GeneratedSymbolIds.AddRange(symbols);

            if (isWinningGame)
            {
                // If the game was a winner, record the index of the prize tier that was won.
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Fills a grid (or part of a grid) with symbols, allowing for "near-misses" where a symbol can appear
        /// up to (requiredMatches - 1) times, but never enough to form a winning combination.
        /// </summary>
        /// <param name="grid">The integer array representing the grid to be filled.</param>
        /// <param name="symbolPool">The list of symbol IDs available to use.</param>
        /// <param name="requiredMatches">The number of matches required for a win.</param>
        /// <param name="random">The shared Random instance.</param>
        private void FillWithNearMisses(int[] grid, List<int> symbolPool, int requiredMatches, Random random)
        {
            if (requiredMatches < 2) requiredMatches = 2; // Safety check for the logic.

            // Validate if a non-winning panel is even possible with the given constraints.
            // This prevents an infinite loop if symbol variety is too low.
            if (symbolPool.Distinct().Count() < grid.Length && (requiredMatches - 1) * symbolPool.Distinct().Count() < grid.Length)
            {
                throw new InvalidOperationException($"Cannot generate a non-winning panel for {ModuleName}. The number of unique symbols ({symbolPool.Distinct().Count()}) is too low for the grid size ({grid.Length}) and the 'Match {requiredMatches}' rule.");
            }

            var symbolCounts = new Dictionary<int, int>();
            for (int i = 0; i < grid.Length; i++)
            {
                // Find all symbols that can still be placed without causing an accidental win.
                var eligibleSymbols = symbolPool.Where(s => !symbolCounts.ContainsKey(s) || symbolCounts[s] < (requiredMatches - 1)).ToList();

                // If we run out of eligible symbols (a rare edge case), reset the pool and counts to avoid getting stuck.
                if (!eligibleSymbols.Any())
                {
                    Shuffle(symbolPool, random);
                    eligibleSymbols = new List<int>(symbolPool);
                    symbolCounts.Clear();
                }

                int symbolToAdd = eligibleSymbols[random.Next(eligibleSymbols.Count)];
                grid[i] = symbolToAdd;

                // Update the count for the symbol that was just added.
                if (symbolCounts.ContainsKey(symbolToAdd)) symbolCounts[symbolToAdd]++;
                else symbolCounts.Add(symbolToAdd, 1);
            }
        }

        /// <summary>
        /// Fills a grid with unique symbols, ensuring no duplicates and therefore no near-misses.
        /// </summary>
        /// <param name="grid">The integer array representing the grid to be filled.</param>
        /// <param name="symbolPool">The list of symbol IDs available to use.</param>
        /// <param name="random">The shared Random instance.</param>
        private void FillWithNoNearMisses(int[] grid, List<int> symbolPool, Random random)
        {
            // A panel with no near misses requires at least as many unique symbols as there are slots in the grid.
            if (symbolPool.Distinct().Count() < grid.Length)
            {
                throw new InvalidOperationException($"Cannot generate a panel for {ModuleName} with no near misses. The number of unique symbols ({symbolPool.Distinct().Count()}) is less than the number of required slots ({grid.Length}).");
            }

            // Get a shuffled, distinct list of symbols and take the first 'grid.Length' items.
            var shuffledSymbols = symbolPool.Distinct().OrderBy(s => random.Next()).ToList();
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = shuffledSymbols[i];
            }
        }

        /// <summary>
        /// Finds the symbol associated with the highest non-online prize tier.
        /// This is used to prevent near-misses of the top prize on losing tickets.
        /// </summary>
        /// <param name="project">The project context.</param>
        /// <returns>The ID of the symbol, or -1 if not found.</returns>
        private int GetHighestValueSymbol(ScratchCardProject project)
        {
            var highestPrize = project.PrizeTiers
                .Where(p => !p.IsOnlinePrize && p.Value > 0)
                .OrderByDescending(p => p.Value)
                .FirstOrDefault();

            if (highestPrize == null) return -1;

            // This logic relies on a convention where the symbol's internal 'Name' (e.g., "ONETHOU")
            // matches the prize's 'TextCode'.
            var symbol = project.AvailableSymbols.FirstOrDefault(s => s.Name == highestPrize.TextCode);
            return symbol?.Id ?? -1;
        }

        /// <summary>
        /// Shuffles an integer array in-place using the Fisher-Yates algorithm.
        /// </summary>
        /// <param name="array">The array to shuffle.</param>
        /// <param name="random">The shared Random instance.</param>
        private void Shuffle(int[] array, Random random)
        {
            int n = array.Length;
            while (n > 1) { n--; int k = random.Next(n + 1); (array[k], array[n]) = (array[n], array[k]); }
        }

        /// <summary>
        /// Shuffles an integer list in-place using the Fisher-Yates algorithm.
        /// </summary>
        /// <param name="list">The list to shuffle.</param>
        /// <param name="random">The shared Random instance.</param>
        private void Shuffle(List<int> list, Random random)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols for legacy binary file formats.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <returns>A byte array of symbol data.</returns>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            // This implementation is hardcoded to a 9-byte format for compatibility with a legacy system.
            var data = new byte[9];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    // Pad with 0 if there are fewer symbols than the required byte array length.
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int i = 1; i <= Rows * Columns; i++)
            {
                // Each symbol position gets a block of columns in the CSV for detailed reporting.
                headers.Add($"G{GameNumber}Sym{i}");       // The raw symbol ID
                headers.Add($"G{GameNumber}SymName{i}");  // The human-readable symbol name (e.g., "Gold Bar")
                headers.Add($"G{GameNumber}S{i}");         // The prize text displayed underneath (e.g., "£50")
                headers.Add($"G{GameNumber}P{i}");         // The pence value of the prize (a legacy field)
                headers.Add($"G{GameNumber}T{i}");         // The prize text code (e.g., "FIFTY")
                headers.Add($"G{GameNumber}Status{i}");    // "WIN" or "NO WIN" for this specific position
            }
            return headers;
        }

        /// <summary>
        /// Gets the data for this game as a list of strings for a single row in the final CSV report.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up names and prizes.</param>
        /// <returns>A list of strings representing the cell values for this game.</returns>
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
                    if (winningGroup != null)
                    {
                        winningSymbolId = winningGroup.Key;
                    }
                }

                // Create a pool of decoy prizes to display under non-winning symbols in the CSV.
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
                            // Otherwise, add data from a randomly selected decoy prize for verisimilitude.
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
                                // If no decoy prizes are available, add empty strings as a fallback.
                                rowData.Add("\"\"");
                                rowData.Add("\"\"");
                                rowData.Add("\"\"");
                                rowData.Add("\"NO WIN\"");
                            }
                        }
                    }
                    else
                    {
                        // Pad with empty data if the grid is larger than the number of generated symbols.
                        rowData.Add("0");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion

    #region Find Winning Symbol Game

    /// <summary>
    /// Represents a game where the player reveals a set of symbols and wins if a specific, pre-defined "Winning Symbol" is present.
    /// </summary>
    public class FindWinningSymbolGame : GameModule
    {
        #region Private Fields

        /// <summary>
        /// A separate Random instance used specifically for CSV generation to avoid interfering with the main gameplay generation sequence.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the total number of symbols to be displayed in the game panel.
        /// </summary>
        public int NumberOfSymbols { get; set; } = 6;

        /// <summary>
        /// Gets or sets the ID of the symbol that constitutes a win if found.
        /// This is configured by the user in the properties pane.
        /// </summary>
        public int WinningSymbolId { get; set; }

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Find Winning Symbol game instance on a single ticket.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
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

            // Create a pool of "losing" symbols by excluding the designated winning symbol.
            var losingSymbolPool = project.AvailableSymbols.Where(s => s.Id != WinningSymbolId).Select(s => s.Id).ToList();

            // If there are no losing symbols to act as decoys, a winning game cannot be generated. Downgrade to a losing game.
            if (!losingSymbolPool.Any() && isWinningGame)
            {
                isWinningGame = false;
            }

            if (isWinningGame)
            {
                // --- Winning Game Logic ---
                // 1. Add the winning symbol to the list.
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
                // This game type does not have "near misses" in the same way a grid game does.
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
        /// <param name="random">The shared Random instance.</param>
        private void Shuffle(List<int> list, Random random)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols for legacy binary file formats.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <returns>A byte array of symbol data.</returns>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            var data = new byte[NumberOfSymbols];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < NumberOfSymbols; i++)
                {
                    // Pad with 0 if there are fewer symbols than the required byte array length.
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int i = 1; i <= NumberOfSymbols; i++)
            {
                // Each symbol position gets a block of columns in the CSV for detailed reporting.
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
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up names and prizes.</param>
        /// <returns>A list of strings representing the cell values for this game.</returns>
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

                        // If this game is a winner and the current symbol is the winning symbol, add the prize data.
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
                            // Otherwise, add decoy prize data.
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
                                rowData.Add("\"\"");
                                rowData.Add("\"\"");
                                rowData.Add("\"\"");
                                rowData.Add("\"NO WIN\"");
                            }
                        }
                    }
                    else
                    {
                        // Pad with empty data if necessary.
                        rowData.Add("0");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion

    #region Match Prizes In Grid Game

    /// <summary>
    /// Represents a game where the player must find a certain number of matching prize values within a grid to win.
    /// This is a variation of the symbol matching game, operating on prize tiers instead.
    /// </summary>
    public class MatchPrizesInGridGame : GameModule
    {
        #region Properties

        /// <summary>
        /// Gets or sets the number of rows in the game grid.
        /// </summary>
        public int Rows { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of columns in the game grid.
        /// </summary>
        public int Columns { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of identical prize values required to constitute a win.
        /// </summary>
        public int PrizesToMatch { get; set; } = 3;

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Match Prizes in Grid game instance on a single ticket.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            int gridSize = Rows * Columns;
            var prizeIndices = new int[gridSize];

            // Create a pool of valid, winnable, offline prize indices to draw from.
            var prizePool = project.PrizeTiers
                .Select((p, i) => new { Prize = p, Index = i })
                .Where(x => x.Prize.Value > 0 && !x.Prize.IsOnlinePrize)
                .Select(x => x.Index)
                .ToList();

            // If no valid prizes exist to populate the grid, exit gracefully.
            if (!prizePool.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            if (isWinningGame)
            {
                // --- Winning Game Logic ---
                // 1. Find the index of the specific prize tier this game is supposed to award.
                int winningPrizeIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);

                PrizeTier winningPrize = project.PrizeTiers[winningPrizeIndex];
                // Create a pool of decoy prizes, excluding the winning prize value to prevent accidental wins.
                var decoyPrizePool = prizePool.Where(pIndex => project.PrizeTiers[pIndex].Value != winningPrize.Value).ToList();

                // If there are no decoy prizes, a win cannot be unambiguously generated. Downgrade to a losing panel.
                if (!decoyPrizePool.Any())
                {
                    isWinningGame = false;
                }
                else
                {
                    // 2. Place the required number of winning prize indices into the grid.
                    for (int i = 0; i < PrizesToMatch; i++)
                    {
                        prizeIndices[i] = winningPrizeIndex;
                    }

                    // 3. Fill the rest of the grid with decoy prizes.
                    var decoyGridPart = new int[gridSize - PrizesToMatch];

                    // Special Rule: For "Match 2" games, the decoy grid part must contain all unique prizes
                    // to prevent ambiguity for the player.
                    if (PrizesToMatch == 2)
                    {
                        if (decoyPrizePool.Distinct().Count() < decoyGridPart.Length)
                        {
                            throw new InvalidOperationException($"Cannot generate a winning 'Match 2' prize game: not enough unique decoy prizes for the grid size.");
                        }
                        FillWithNoNearMisses(decoyGridPart, decoyPrizePool, random);
                    }
                    else
                    {
                        // For "Match 3" or higher, decoys can have duplicates, so we just ensure no accidental wins.
                        FillWithNoNearMisses(decoyGridPart, decoyPrizePool, random);
                    }

                    // 4. Copy decoys and shuffle the final grid.
                    Array.Copy(decoyGridPart, 0, prizeIndices, PrizesToMatch, decoyGridPart.Length);
                    Shuffle(prizeIndices, random);
                }
            }

            if (!isWinningGame)
            {
                // --- Losing Game Logic ---
                if (PrizesToMatch == 2 || ticket.WinPrize.Value > 0)
                {
                    // For "Match 2" games or any losing panel on an otherwise winning ticket, we must avoid near misses.
                    FillWithNoNearMisses(prizeIndices, prizePool, random);
                }
                else
                {
                    // For "Match 3"+ on a completely losing ticket, near misses are allowed.
                    // We exclude the highest value prize from the pool to avoid a jackpot near-miss.
                    var highestPrize = project.PrizeTiers.Where(p => !p.IsOnlinePrize).OrderByDescending(p => p.Value).FirstOrDefault();
                    var filteredPrizePool = prizePool;
                    if (highestPrize != null)
                    {
                        int highestPrizeIndex = project.PrizeTiers.ToList().IndexOf(highestPrize);
                        filteredPrizePool = prizePool.Where(p => p != highestPrizeIndex).ToList();
                    }
                    FillWithNearMisses(prizeIndices, filteredPrizePool.Any() ? filteredPrizePool : prizePool, PrizesToMatch, random);
                }
            }

            // The GeneratedSymbolIds list is repurposed here to store prize indices.
            playData.GeneratedSymbolIds.AddRange(prizeIndices);
            if (isWinningGame) { playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize); }
            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Fills a grid with prize indices, allowing for near-misses (e.g., two identical prizes in a "Match 3" game).
        /// </summary>
        /// <param name="grid">The integer array representing the grid to be filled with prize indices.</param>
        /// <param name="prizePool">The list of eligible prize indices.</param>
        /// <param name="requiredMatches">The number of matches required for a win.</param>
        /// <param name="random">The shared Random instance.</param>
        private void FillWithNearMisses(int[] grid, List<int> prizePool, int requiredMatches, Random random)
        {
            if (requiredMatches < 2) requiredMatches = 2; // Safety check.

            var prizeCounts = new Dictionary<int, int>();
            for (int i = 0; i < grid.Length; i++)
            {
                // Find all prize indices that can still be placed without causing a win.
                var eligiblePrizes = prizePool.Where(p => !prizeCounts.ContainsKey(p) || prizeCounts[p] < (requiredMatches - 1)).ToList();
                if (!eligiblePrizes.Any())
                {
                    // Fallback if we run out of options, to prevent getting stuck.
                    eligiblePrizes = prizePool;
                    prizeCounts.Clear();
                }
                int prizeToAdd = eligiblePrizes[random.Next(eligiblePrizes.Count)];
                grid[i] = prizeToAdd;

                if (prizeCounts.ContainsKey(prizeToAdd))
                    prizeCounts[prizeToAdd]++;
                else
                    prizeCounts.Add(prizeToAdd, 1);
            }
        }

        /// <summary>
        /// Fills a grid with unique prize indices, ensuring no duplicates.
        /// </summary>
        /// <param name="grid">The integer array representing the grid to be filled.</param>
        /// <param name="prizePool">The list of prize indices available to use.</param>
        /// <param name="random">The shared Random instance.</param>
        private void FillWithNoNearMisses(int[] grid, List<int> prizePool, Random random)
        {
            if (prizePool.Distinct().Count() < grid.Length)
            {
                throw new InvalidOperationException($"Cannot generate a prize panel with no near misses. The number of unique prizes ({prizePool.Distinct().Count()}) is less than the number of required slots ({grid.Length}).");
            }
            // Take a distinct, shuffled set of prize indices to fill the grid.
            var shuffledPrizes = prizePool.Distinct().OrderBy(p => random.Next()).ToList();
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = shuffledPrizes[i];
            }
        }

        /// <summary>
        /// Shuffles an integer array in-place using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle(int[] array, Random random)
        {
            int n = array.Length;
            while (n > 1) { n--; int k = random.Next(n + 1); (array[k], array[n]) = (array[n], array[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols (prize indices) for legacy binary file formats.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <returns>A byte array of prize index data.</returns>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            // This implementation is hardcoded to a 9-byte format.
            var data = new byte[9];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// This version is simpler as it doesn't need separate columns for symbol names.
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
        /// <param name="project">The project context for looking up prize details.</param>
        /// <returns>A list of strings representing the cell values for this game.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
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
                        // The "SymbolId" is actually the index into the PrizeTiers collection.
                        int prizeIndex = playData.GeneratedSymbolIds[i];
                        PrizeTier prize = project.PrizeTiers[prizeIndex];
                        string pence = (prize.Value > 0 && prize.Value < 100) ? ".00" : "";

                        rowData.Add($"\"{prize.DisplayText}\"");
                        rowData.Add($"\"{pence}\"");
                        rowData.Add($"\"{prize.TextCode}\"");

                        // Determine if this specific prize is part of the winning set.
                        bool isWinningPrize = isWinningGame && playData.GeneratedSymbolIds.Where(p => p == prizeIndex).Count() >= PrizesToMatch;
                        rowData.Add(isWinningPrize ? "\"WIN\"" : "\"NO WIN\"");
                    }
                    else
                    {
                        // Pad with empty data if necessary.
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion

    #region Match Symbols In Row Game

    /// <summary>
    /// Represents a game with multiple independent rows, where a win occurs if a single row
    /// contains the required number of matching symbols.
    /// </summary>
    public class MatchSymbolsInRowGame : GameModule
    {
        #region Private Fields

        /// <summary>
        /// A separate Random instance used specifically for CSV generation to avoid interfering with the main gameplay generation sequence.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the total number of independent rows in the game.
        /// </summary>
        public int NumberOfRows { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of symbols that appear in each row.
        /// </summary>
        public int SymbolsPerRow { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of identical symbols required within a single row to constitute a win.
        /// </summary>
        public int SymbolsToMatchInRow { get; set; } = 3;

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Match Symbols in Row game instance.
        /// It constructs the game panel row by row, designating one row as the winner if required.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var allSymbols = new List<int>();

            // Exit gracefully if no symbols are available.
            if (project.AvailableSymbols == null || !project.AvailableSymbols.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            // If this is a winning game, randomly select which row will contain the winning combination.
            int winningRow = isWinningGame ? random.Next(0, NumberOfRows) : -1;

            // Generate each row independently.
            for (int r = 0; r < NumberOfRows; r++)
            {
                bool rowIsWinner = (r == winningRow);
                allSymbols.AddRange(GenerateRow(rowIsWinner, project, ticket, random));
            }

            playData.GeneratedSymbolIds.AddRange(allSymbols);

            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Generates the symbol data for a single row, which can be either a winning or a losing combination.
        /// </summary>
        /// <param name="isWinningRow">A boolean indicating if this row should be a winner.</param>
        /// <param name="project">The project context.</param>
        /// <param name="ticket">The parent ticket, used to determine near-miss rules.</param>
        /// <param name="random">The shared Random instance.</param>
        /// <returns>A list of symbol IDs for the generated row.</returns>
        private List<int> GenerateRow(bool isWinningRow, ScratchCardProject project, Ticket ticket, Random random)
        {
            var rowSymbols = new int[SymbolsPerRow];
            var symbolPool = project.AvailableSymbols.Select(s => s.Id).ToList();

            if (isWinningRow)
            {
                int winningSymbolId = symbolPool[random.Next(symbolPool.Count)];
                var decoySymbolPool = symbolPool.Where(s => s != winningSymbolId).ToList();

                // If no decoys are available, downgrade to a losing row.
                if (!decoySymbolPool.Any())
                {
                    isWinningRow = false;
                }
                else
                {
                    // Place the winning symbols.
                    for (int i = 0; i < SymbolsToMatchInRow; i++)
                    {
                        rowSymbols[i] = winningSymbolId;
                    }
                    // Fill the rest of the row with non-winning symbols.
                    var decoyPart = new int[SymbolsPerRow - SymbolsToMatchInRow];
                    FillWithNoNearMisses(decoyPart, decoySymbolPool, random);

                    Array.Copy(decoyPart, 0, rowSymbols, SymbolsToMatchInRow, decoyPart.Length);
                    Shuffle(rowSymbols, random);
                }
            }

            if (!isWinningRow)
            {
                // The logic for generating a losing row is identical to the grid-based game,
                // applied here on a smaller, row-sized scale.
                if (ticket.WinPrize.Value > 0)
                {
                    FillWithNoNearMisses(rowSymbols, symbolPool, random);
                }
                else
                {
                    if (SymbolsToMatchInRow <= 2)
                    {
                        FillWithNoNearMisses(rowSymbols, symbolPool, random);
                    }
                    else
                    {
                        var highestValueSymbol = GetHighestValueSymbol(project);
                        var filteredSymbolPool = highestValueSymbol == -1 ? symbolPool : symbolPool.Where(s => s != highestValueSymbol).ToList();
                        FillWithNearMisses(rowSymbols, filteredSymbolPool.Any() ? filteredSymbolPool : symbolPool, SymbolsToMatchInRow, random);
                    }
                }
            }
            return rowSymbols.ToList();
        }

        /// <summary>
        /// Fills a grid with symbols, allowing for "near-misses".
        /// </summary>
        private void FillWithNearMisses(int[] grid, List<int> symbolPool, int requiredMatches, Random random)
        {
            var symbolCounts = new Dictionary<int, int>();
            for (int i = 0; i < grid.Length; i++)
            {
                var eligibleSymbols = symbolPool
                    .Where(s => !symbolCounts.ContainsKey(s) || symbolCounts[s] < (requiredMatches - 1))
                    .ToList();
                if (!eligibleSymbols.Any())
                {
                    eligibleSymbols = symbolPool; // Fallback to reset pool if options are exhausted.
                }
                int symbolToAdd = eligibleSymbols[random.Next(eligibleSymbols.Count)];
                grid[i] = symbolToAdd;
                if (symbolCounts.ContainsKey(symbolToAdd))
                    symbolCounts[symbolToAdd]++;
                else
                    symbolCounts.Add(symbolToAdd, 1);
            }
        }

        /// <summary>
        /// Fills a grid with unique symbols to ensure no near-misses.
        /// </summary>
        private void FillWithNoNearMisses(int[] grid, List<int> symbolPool, Random random)
        {
            var symbolCounts = new Dictionary<int, int>();
            for (int i = 0; i < grid.Length; i++)
            {
                var eligibleSymbols = symbolPool
                    .Where(s => !symbolCounts.ContainsKey(s))
                    .ToList();
                if (!eligibleSymbols.Any())
                {
                    Shuffle(symbolPool, random);
                    eligibleSymbols = new List<int>(symbolPool);
                    symbolCounts.Clear();
                }
                int symbolToAdd = eligibleSymbols[random.Next(eligibleSymbols.Count)];
                grid[i] = symbolToAdd;
                symbolCounts.Add(symbolToAdd, 1);
            }
        }

        /// <summary>
        /// Finds the symbol associated with the highest non-online prize tier.
        /// </summary>
        private int GetHighestValueSymbol(ScratchCardProject project)
        {
            var highestPrize = project.PrizeTiers
                .Where(p => !p.IsOnlinePrize && p.Value > 0)
                .OrderByDescending(p => p.Value)
                .FirstOrDefault();
            if (highestPrize == null) return -1;
            var symbol = project.AvailableSymbols.FirstOrDefault(s => s.Name == highestPrize.TextCode);
            return symbol?.Id ?? -1;
        }

        /// <summary>
        /// Shuffles an integer array in-place using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle(int[] array, Random random)
        {
            int n = array.Length;
            // Fisher-Yates shuffle algorithm
            // This algorithm iterates through the array, swapping each element with a randomly chosen element that comes before it (including itself).
            // This ensures that each possible permutation of the array is equally likely.
            while (n > 1) { n--; int k = random.Next(n + 1); (array[k], array[n]) = (array[n], array[k]); }
        }

        /// <summary>
        /// Shuffles an integer list in-place using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle(List<int> list, Random random)
        {
            int n = list.Count;
            // Fisher-Yates shuffle algorithm
            // This algorithm iterates through the list, swapping each element with a randomly chosen element that comes before it (including itself).
            // This ensures that each possible permutation of the list is equally likely.
            while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols for legacy binary file formats.
        /// </summary>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            // Note: This legacy format assumes a 9-byte structure, which may not align
            // with all possible row/symbol configurations of this game.
            var data = new byte[9];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// Headers are formatted to distinguish between rows (e.g., G1Sym1_1, G1Sym1_2...).
        /// </summary>
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
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                bool isWinningGame = playData.PrizeTierIndex >= 0;
                PrizeTier winPrize = isWinningGame ? project.PrizeTiers[playData.PrizeTierIndex] : null;
                int winningRow = -1;

                // If the ticket is a winner for this game, we must first identify which row contains the win.
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

                            // If this is the winning row, display the actual prize information.
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
                                // Otherwise, display a random decoy prize.
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
                                    rowData.Add("\"\"");
                                    rowData.Add("\"\"");
                                    rowData.Add("\"\"");
                                    rowData.Add("\"NO WIN\"");
                                }
                            }
                        }
                        else
                        {
                            // Pad with empty data if necessary.
                            rowData.Add("0");
                            rowData.Add("\"\"");
                            rowData.Add("\"\"");
                            rowData.Add("\"\"");
                            rowData.Add("\"\"");
                            rowData.Add("\"\"");
                        }
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion

    #region Match Prizes In Row Game

    /// <summary>
    /// Represents a game with multiple independent rows, where a win occurs if a single row
    /// contains the required number of matching prize values.
    /// </summary>
    public class MatchPrizesInRowGame : GameModule
    {
        #region Properties

        /// <summary>
        /// Gets or sets the total number of independent rows in the game.
        /// </summary>
        public int NumberOfRows { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of prizes that appear in each row.
        /// </summary>
        public int PrizesPerRow { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of identical prizes required within a single row to constitute a win.
        /// </summary>
        public int PrizesToMatchInRow { get; set; } = 3;

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Match Prizes in Row game instance.
        /// It constructs the game panel row by row, designating one as the winner if required.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var allPrizeIndices = new List<int>();

            // Exit gracefully if no winnable prize tiers are configured.
            if (project.PrizeTiers == null || !project.PrizeTiers.Any(p => p.Value > 0))
            {
                ticket.GameData.Add(playData);
                return;
            }

            // If this is a winning game, randomly select which row will be the winner.
            int winningRow = isWinningGame ? random.Next(0, NumberOfRows) : -1;

            // Generate each row of prize indices independently.
            for (int r = 0; r < NumberOfRows; r++)
            {
                var rowIsWinner = (r == winningRow);
                allPrizeIndices.AddRange(GeneratePrizeRow(rowIsWinner, winTier, project, ticket, random));
            }

            // "GeneratedSymbolIds" is used here to store the list of prize indices.
            playData.GeneratedSymbolIds.AddRange(allPrizeIndices);

            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Generates the prize index data for a single row.
        /// </summary>
        /// <param name="isWinningRow">A boolean indicating if this row should be a winning combination.</param>
        /// <param name="winningTier">The prize tier to award if this is a winning row.</param>
        /// <param name="project">The project context.</param>
        /// <param name="ticket">The parent ticket, used to determine near-miss rules.</param>
        /// <param name="random">The shared Random instance.</param>
        /// <returns>A list of prize indices for the generated row.</returns>
        private List<int> GeneratePrizeRow(bool isWinningRow, PrizeTier winningTier, ScratchCardProject project, Ticket ticket, Random random)
        {
            var rowPrizeIndices = new int[PrizesPerRow];
            // Create a pool of valid, winnable, offline prize indices.
            var prizePool = project.PrizeTiers
                .Select((p, i) => new { Index = i, Prize = p })
                .Where(x => x.Prize.Value > 0 && !x.Prize.IsOnlinePrize)
                .Select(x => x.Index)
                .ToList();

            if (isWinningRow)
            {
                int winningPrizeIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winningTier.Value && p.IsOnlinePrize == winningTier.IsOnlinePrize);
                PrizeTier winningPrize = project.PrizeTiers[winningPrizeIndex];
                var decoyPrizePool = prizePool.Where(pIndex => project.PrizeTiers[pIndex].Value != winningPrize.Value).ToList();

                if (!decoyPrizePool.Any())
                {
                    isWinningRow = false; // Downgrade if no decoys are available.
                }
                else
                {
                    // Place the winning prize indices.
                    for (int i = 0; i < PrizesToMatchInRow; i++)
                    {
                        rowPrizeIndices[i] = winningPrizeIndex;
                    }
                    // Fill the rest of the row with non-winning decoy prizes.
                    var decoyPart = new int[PrizesPerRow - PrizesToMatchInRow];
                    FillWithNoNearMisses(decoyPart, decoyPrizePool, random);

                    Array.Copy(decoyPart, 0, rowPrizeIndices, PrizesToMatchInRow, decoyPart.Length);
                    Shuffle(rowPrizeIndices, random);
                }
            }

            if (!isWinningRow)
            {
                // Generate a losing row using the same logic as other matching games.
                if (ticket.WinPrize.Value > 0 || PrizesToMatchInRow <= 2)
                {
                    // No near-misses on a winning ticket or for "Match 2" style games.
                    FillWithNoNearMisses(rowPrizeIndices, prizePool, random);
                }
                else
                {
                    // Allow near-misses on a losing ticket, but not for the top prize.
                    var highestPrize = project.PrizeTiers.Where(p => !p.IsOnlinePrize).OrderByDescending(p => p.Value).FirstOrDefault();
                    var filteredPrizePool = prizePool;
                    if (highestPrize != null)
                    {
                        int highestPrizeIndex = project.PrizeTiers.ToList().IndexOf(highestPrize);
                        filteredPrizePool = prizePool.Where(p => p != highestPrizeIndex).ToList();
                    }
                    FillWithNearMisses(rowPrizeIndices, filteredPrizePool.Any() ? filteredPrizePool : prizePool, PrizesToMatchInRow, random);
                }
            }
            return rowPrizeIndices.ToList();
        }

        /// <summary>
        /// Fills a grid with prize indices, allowing for near-misses.
        /// </summary>
        private void FillWithNearMisses(int[] grid, List<int> prizePool, int requiredMatches, Random random)
        {
            var prizeCounts = new Dictionary<int, int>();
            for (int i = 0; i < grid.Length; i++)
            {
                var eligiblePrizes = prizePool.Where(p => !prizeCounts.ContainsKey(p) || prizeCounts[p] < (requiredMatches - 1)).ToList();
                if (!eligiblePrizes.Any())
                {
                    eligiblePrizes = prizePool;
                    prizeCounts.Clear();
                }
                int prizeToAdd = eligiblePrizes[random.Next(eligiblePrizes.Count)];
                grid[i] = prizeToAdd;
                if (prizeCounts.ContainsKey(prizeToAdd))
                    prizeCounts[prizeToAdd]++;
                else
                    prizeCounts.Add(prizeToAdd, 1);
            }
        }

        /// <summary>
        /// Fills a grid with unique prize indices to ensure no near-misses.
        /// </summary>
        private void FillWithNoNearMisses(int[] grid, List<int> prizePool, Random random)
        {
            if (prizePool.Distinct().Count() < grid.Length)
            {
                throw new InvalidOperationException($"Cannot generate a panel with no near misses. The number of unique prizes ({prizePool.Distinct().Count()}) is less than the number of required slots ({grid.Length}).");
            }
            var shuffledPrizes = prizePool.Distinct().OrderBy(p => random.Next()).ToList();
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = shuffledPrizes[i];
            }
        }

        /// <summary>
        /// Shuffles an integer array in-place using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle(int[] array, Random random)
        {
            int n = array.Length;
            while (n > 1) { n--; int k = random.Next(n + 1); (array[k], array[n]) = (array[n], array[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols (prize indices) for legacy binary file formats.
        /// </summary>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            // Note: This legacy format assumes a 9-byte structure.
            var data = new byte[9];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
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
                        var rowSymbols = playData.GeneratedSymbolIds.Skip(r * PrizesPerRow).Take(PrizesPerRow).ToList();
                        if (rowSymbols.GroupBy(s => s).Any(g => g.Count() >= PrizesToMatchInRow))
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

                        int currentRow = i / PrizesPerRow;
                        // Mark the status as "WIN" only for prizes in the winning row.
                        rowData.Add(isWinningTicket && currentRow == winningRow ? "\"WIN\"" : "\"NO WIN\"");
                    }
                    else
                    {
                        // Pad with empty data if necessary.
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion

    #region Online Bonus Game

    /// <summary>
    /// Represents a special game module for online bonus prizes, typically represented by a QR code on the physical card.
    /// This module does not generate playable symbols; instead, it acts as the designated winner for any prize tier
    /// that is marked with the 'IsOnlinePrize' flag.
    /// </summary>
    public class OnlineBonusGame : GameModule
    {
        #region Private Fields

        /// <summary>
        /// The backing field for the Url property.
        /// </summary>
        private string _url;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the base URL that will be encoded in the QR code.
        /// A unique security code is typically appended to this URL during the final file generation stage.
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
        /// <param name="random">A shared Random instance (not used in this implementation but required by the abstract class).</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };

            // If this module is the winner, we simply record which prize was won.
            // No symbols are generated.
            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game. For the Online Bonus Game, this is minimal
        /// as it does not have traditional symbols. Returns a single byte as a placeholder.
        /// </summary>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <returns>A placeholder byte array.</returns>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            // The legacy format may require a non-empty array, so we return a single byte.
            return new byte[1];
        }

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
        /// <returns>A list of strings containing the base URL and a sample code placeholder.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            // Note: The final, unique security code for the QR code is generated in the FileGenerationService
            // during the 'Generate Combined Files' step. A placeholder is used here.
            return new List<string> { $"\"{Url ?? string.Empty}\"", "\"SAMPLE_CODE\"" };
        }

        #endregion
    }

    #endregion

    #region Christmas Tree Game

    /// <summary>
    /// Represents a themed "Find Winning Symbol" game with a fixed layout of 15 symbols arranged in a Christmas tree shape.
    /// The gameplay is identical to FindWinningSymbolGame, but the number of symbols is not configurable.
    /// </summary>
    public class ChristmasTreeGame : GameModule
    {
        #region Private Fields

        /// <summary>
        /// A separate Random instance used specifically for CSV generation to avoid interfering with the main gameplay generation sequence.
        /// </summary>
        private static Random _csvRandom = new Random();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the ID of the symbol that constitutes a win if found.
        /// </summary>
        public int WinningSymbolId { get; set; }

        /// <summary>
        /// Defines the fixed layout of the game panel, representing rows with an increasing number of symbols
        /// to form a tree/pyramid shape. The sum of these values is the total number of symbols.
        /// </summary>
        private readonly int[] _rowLayout = { 1, 2, 3, 4, 5 }; // Total = 15 symbols

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Christmas Tree game instance on a single ticket.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var allSymbols = new List<int>();

            // Exit gracefully if no symbols are available.
            if (project.AvailableSymbols == null || !project.AvailableSymbols.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            var symbolPool = project.AvailableSymbols.Select(s => s.Id).ToList();
            var losingSymbolPool = symbolPool.Where(s => s != WinningSymbolId).ToList();
            int totalSymbols = _rowLayout.Sum(); // Total symbols is fixed by the layout.

            if (isWinningGame)
            {
                // Downgrade to a losing game if no decoy symbols are available.
                if (!losingSymbolPool.Any())
                {
                    isWinningGame = false;
                }
                else
                {
                    // --- Winning Game Logic ---
                    // 1. Add the winning symbol.
                    allSymbols.Add(WinningSymbolId);

                    // 2. Fill the remaining slots with random losing symbols.
                    for (int i = 0; i < totalSymbols - 1; i++)
                    {
                        allSymbols.Add(losingSymbolPool[random.Next(losingSymbolPool.Count)]);
                    }

                    // 3. Shuffle the final list to randomise the winning symbol's position.
                    Shuffle(allSymbols, random);
                }
            }

            if (!isWinningGame)
            {
                // --- Losing Game Logic ---
                // A losing game is simply a list of symbols drawn exclusively from the losing pool.
                for (int i = 0; i < totalSymbols; i++)
                {
                    if (losingSymbolPool.Any())
                    {
                        allSymbols.Add(losingSymbolPool[random.Next(losingSymbolPool.Count)]);
                    }
                    else
                    {
                        // Fallback in the extreme edge case where only one symbol (the winning one) exists.
                        allSymbols.Add(symbolPool.First());
                    }
                }
            }

            playData.GeneratedSymbolIds.AddRange(allSymbols);
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
        /// <param name="random">The shared Random instance.</param>
        private void Shuffle(List<int> list, Random random)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols for legacy binary file formats.
        /// </summary>
        /// <remarks>
        /// WARNING: This legacy format is fixed at 9 bytes, but this game generates 15 symbols.
        /// The generated binary data will be truncated.
        /// </remarks>
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <returns>A truncated 9-byte array of symbol data.</returns>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            var data = new byte[9];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
        /// </summary>
        /// <returns>A list of strings representing the column headers.</returns>
        public override List<string> GetCsvHeaders()
        {
            var headers = new List<string>();
            for (int i = 1; i <= _rowLayout.Sum(); i++)
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
        /// <param name="ticket">The ticket containing the generated play data.</param>
        /// <param name="project">The project context for looking up names and prizes.</param>
        /// <returns>A list of strings representing the cell values for this game.</returns>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);

            if (playData != null)
            {
                bool isWinningGame = playData.PrizeTierIndex >= 0;
                PrizeTier winPrize = isWinningGame ? project.PrizeTiers[playData.PrizeTierIndex] : null;

                var decoyPrizes = project.PrizeTiers.Where(p => p.Value >= project.Settings.TicketSalePrice).ToList();
                int totalSymbols = _rowLayout.Sum();

                for (int i = 0; i < totalSymbols; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        int symbolId = playData.GeneratedSymbolIds[i];
                        string symbolName = project.AvailableSymbols.FirstOrDefault(s => s.Id == symbolId)?.DisplayText ?? "N/A";

                        rowData.Add(symbolId.ToString());
                        rowData.Add($"\"{symbolName}\"");

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
                                rowData.Add("\"\"");
                                rowData.Add("\"\"");
                                rowData.Add("\"\"");
                                rowData.Add("\"NO WIN\"");
                            }
                        }
                    }
                    else
                    {
                        // Pad with empty data if necessary.
                        rowData.Add("0");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion

    #region Match Symbol To Prize Game

    /// <summary>
    /// Represents a special game where finding a specific "winning symbol" awards a prize that is
    /// directly linked to that symbol by name. For example, finding the symbol "TEN" wins the prize with the TextCode "TEN".
    /// This module uses the dedicated 'NumericSymbols' collection from the project.
    /// </summary>
    public class MatchSymbolToPrizeGame : GameModule
    {
        #region Properties

        /// <summary>
        /// Gets or sets the total number of symbols to be displayed in the game panel.
        /// </summary>
        public int NumberOfSymbols { get; set; } = 6;

        /// <summary>
        /// Gets or sets the ID of the symbol that, when found, awards a corresponding prize.
        /// </summary>
        public int WinningSymbolId { get; set; }

        #endregion

        #region Gameplay Generation

        /// <summary>
        /// Generates the play data for a Match Symbol to Prize game instance.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game. Note: This game's logic may override this.</param>
        /// <param name="project">The project context for accessing symbols and prizes.</param>
        /// <param name="random">A shared Random instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var symbols = new List<int>();

            // This game requires the NumericSymbols collection and a valid WinningSymbolId to be configured.
            if (project.NumericSymbols == null || !project.NumericSymbols.Any() || WinningSymbolId == 0)
            {
                ticket.GameData.Add(playData);
                return;
            }

            // --- Determine the Fixed Prize for This Game ---
            // Unlike other games, this module's prize is determined by its configuration, not the ticket's overall prize.
            var winningSymbol = project.NumericSymbols.FirstOrDefault(s => s.Id == WinningSymbolId);
            var fixedWinTier = project.PrizeTiers.FirstOrDefault(p => p.TextCode == winningSymbol?.Name && !p.IsOnlinePrize);

            // If the configured winning symbol doesn't link to a valid prize, this game can never be a winner.
            if (winningSymbol == null || fixedWinTier == null)
            {
                isWinningGame = false;
            }

            // --- Game-Aware Logic ---
            // To prevent conflicts, we must exclude the winning symbols from other "Match Symbol to Prize" games
            // from being used as decoys in this game.
            var excludedSymbolIds = project.Layout.GameModules
                .OfType<MatchSymbolToPrizeGame>()
                .Where(g => g.GameNumber != this.GameNumber) // Exclude this instance of the game.
                .Select(g => g.WinningSymbolId)
                .ToList();

            if (isWinningGame)
            {
                // --- Winning Game Logic ---
                // 1. Add the configured winning symbol.
                symbols.Add(WinningSymbolId);

                // 2. Create a pool of losing symbols, excluding the winning symbol AND any winning symbols from other games.
                var losingSymbolPool = project.NumericSymbols
                    .Where(s => s.Id != WinningSymbolId && !excludedSymbolIds.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToList();

                // 3. Fill the rest of the panel with decoy symbols.
                for (int i = 0; i < NumberOfSymbols - 1; i++)
                {
                    if (losingSymbolPool.Any())
                    {
                        symbols.Add(losingSymbolPool[random.Next(losingSymbolPool.Count)]);
                    }
                    else
                    {
                        // Fallback: If no decoys are available, this may add extra winning symbols.
                        // This indicates a configuration issue (not enough numeric symbols defined).
                        symbols.Add(WinningSymbolId);
                    }
                }
                Shuffle(symbols, random);
            }
            else
            {
                // --- Losing Game Logic ---
                // A losing panel consists only of symbols from the losing pool.
                var losingSymbolPool = project.NumericSymbols
                    .Where(s => s.Id != WinningSymbolId && !excludedSymbolIds.Contains(s.Id))
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
                // If this game was the winner, record its fixed prize tier index.
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == fixedWinTier.Value && p.IsOnlinePrize == fixedWinTier.IsOnlinePrize);

                // CRITICAL: This game type overrides the ticket's main prize, as its prize is intrinsic to the game's design.
                ticket.WinPrize = fixedWinTier;
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Shuffles an integer list in-place using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle(List<int> list, Random random) { int n = list.Count; while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); } }

        #endregion

        #region File Generation Implementations

        /// <summary>
        /// Gets the raw byte data for this game's symbols for legacy binary file formats.
        /// </summary>
        public override byte[] GetBinarySymbols(Ticket ticket)
        {
            var data = new byte[NumberOfSymbols];
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < NumberOfSymbols; i++)
                {
                    data[i] = (i < playData.GeneratedSymbolIds.Count) ? (byte)playData.GeneratedSymbolIds[i] : (byte)0;
                }
            }
            return data;
        }

        /// <summary>
        /// Gets the header names for this game's columns in the final CSV report.
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
        /// This implementation looks up the prize associated with each symbol to populate the row.
        /// </summary>
        public override List<string> GetCsvRowData(Ticket ticket, ScratchCardProject project)
        {
            var rowData = new List<string>();
            var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == this.GameNumber);
            if (playData != null)
            {
                for (int i = 0; i < NumberOfSymbols; i++)
                {
                    if (i < playData.GeneratedSymbolIds.Count)
                    {
                        int symbolId = playData.GeneratedSymbolIds[i];
                        var symbol = project.NumericSymbols.FirstOrDefault(s => s.Id == symbolId);

                        // Find the prize tier that corresponds to the symbol's name.
                        var prize = project.PrizeTiers.FirstOrDefault(p => !p.IsOnlinePrize && p.TextCode == symbol?.Name);

                        rowData.Add(symbolId.ToString());
                        rowData.Add($"\"{symbol?.DisplayText ?? "N/A"}\"");

                        if (prize != null)
                        {
                            string pence = (prize.Value > 0 && prize.Value < 100) ? ".00" : "";
                            rowData.Add($"\"{prize.DisplayText}\"");
                            rowData.Add($"\"{pence}\"");
                            rowData.Add($"\"{prize.TextCode}\"");
                        }
                        else
                        {
                            // If a symbol doesn't correspond to a prize, it's a decoy.
                            rowData.Add("\"\"");
                            rowData.Add("\"\"");
                            rowData.Add("\"\"");
                        }

                        bool isWinningGame = playData.PrizeTierIndex >= 0;
                        // The status is "WIN" only if this is the winning symbol in a winning game.
                        rowData.Add(isWinningGame && symbolId == this.WinningSymbolId ? "\"WIN\"" : "\"NO WIN\"");
                    }
                    else
                    {
                        // Pad with empty data.
                        rowData.Add("0");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                        rowData.Add("\"\"");
                    }
                }
            }
            return rowData;
        }

        #endregion
    }

    #endregion
}