#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Grid Game Module Base

    /// <summary>
    /// Provides an abstract base class for game modules that involve matching a certain number of items within a grid.
    /// This class uses the Template Method design pattern to encapsulate the core, non-varying algorithm for generating
    /// both winning and losing game panels.
    /// </summary>
    /// <remarks>
    /// The main algorithm is defined in the <see cref="GeneratePlayData"/> method. This "template method" calls several abstract
    /// "primitive operations" (e.g., <see cref="GetItemPool"/>, <see cref="GetWinningItem"/>) that must be implemented by
    /// concrete subclasses. This approach centralises the complex logic of grid generation (handling wins, losses, near-misses,
    /// and shuffling) while allowing subclasses to simply provide the specific data (like symbol IDs or prize indices) required.
    /// This greatly reduces code duplication and improves maintainability.
    /// </remarks>
    public abstract class GridGameModuleBase : GameModule
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
        /// Gets or sets the number of identical items that must be found in the grid to constitute a win.
        /// </summary>
        public int ItemsToMatch { get; set; } = 3;

        #endregion

        #region Abstract Methods (Template Method Pattern Hooks)

        /// <summary>
        /// When overridden in a derived class, gets the complete pool of items (e.g., symbol IDs or prize indices)
        /// available for this game. This is a primitive operation called by the template method.
        /// </summary>
        /// <param name="project">The project context for accessing available symbols or prizes.</param>
        /// <returns>A list of integers representing the complete item pool for the game.</returns>
        protected abstract List<int> GetItemPool(ScratchCardProject project);

        /// <summary>
        /// When overridden in a derived class, gets the specific item that should be used to create a winning panel for a given prize tier.
        /// This is a primitive operation called by the template method.
        /// </summary>
        /// <param name="winTier">The prize tier to be awarded.</param>
        /// <param name="project">The project context.</param>
        /// <param name="random">A shared <see cref="Random"/> instance for operations that require random selection (e.g., choosing a random symbol for a prize).</param>
        /// <returns>An integer representing the specific winning item.</returns>
        protected abstract int GetWinningItem(PrizeTier winTier, ScratchCardProject project, Random random);

        /// <summary>
        /// When overridden in a derived class, gets a pool of "decoy" items, which specifically excludes the winning item
        /// to prevent accidental wins in the non-winning portion of the grid. This is a primitive operation.
        /// </summary>
        /// <param name="winningItem">The winning item to be excluded from the pool.</param>
        /// <param name="fullPool">The full item pool to filter from.</param>
        /// <param name="project">The project context, in case additional filtering rules are needed.</param>
        /// <returns>A list of integers representing the decoy item pool.</returns>
        protected abstract List<int> GetDecoyItemPool(int winningItem, List<int> fullPool, ScratchCardProject project);

        /// <summary>
        /// When overridden in a derived class, gets the item associated with the highest value prize in the game.
        /// This is a primitive operation used by the template method to prevent near-misses of the top prize on losing tickets.
        /// </summary>
        /// <param name="project">The project context.</param>
        /// <returns>The ID of the highest value item, or -1 if not applicable or not found.</returns>
        protected abstract int GetHighestValueItem(ScratchCardProject project);

        #endregion

        #region Gameplay Generation (The Template Method)

        /// <summary>
        /// Generates the play data for a grid-based game instance on a single ticket.
        /// This method contains the core, unchangeable algorithm for constructing both winning and losing panels
        /// by calling the abstract "hook" methods to fetch game-specific data.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared <see cref="Random"/> instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            int gridSize = Rows * Columns;
            var gridItems = new int[gridSize];

            // Step 1: Get the specific item pool from the subclass.
            var itemPool = GetItemPool(project);

            // Gracefully handle cases where no items are defined to prevent exceptions.
            if (itemPool == null || !itemPool.Any())
            {
                ticket.GameData.Add(playData);
                return;
            }

            if (isWinningGame)
            {
                // --- Winning Game Logic ---
                // Step 2a: Get the specific winning and decoy items from the subclass.
                int winningItem = GetWinningItem(winTier, project, random);
                var decoyItemPool = GetDecoyItemPool(winningItem, itemPool, project);

                // If no decoys exist, a win cannot be unambiguously generated. We must downgrade to a losing panel.
                if (!decoyItemPool.Any())
                {
                    isWinningGame = false;
                }
                else
                {
                    // 3. Place the required number of winning items into the grid.
                    for (int i = 0; i < ItemsToMatch; i++)
                    {
                        gridItems[i] = winningItem;
                    }

                    // 4. Fill the rest of the grid with decoy items.
                    var decoyGridPart = new int[gridSize - ItemsToMatch];
                    FillWithDecoys(decoyGridPart, decoyItemPool, random);

                    // 5. Combine and shuffle the grid to randomise item positions.
                    Array.Copy(decoyGridPart, 0, gridItems, ItemsToMatch, decoyGridPart.Length);
                    Shuffle(gridItems, random);
                }
            }

            if (!isWinningGame)
            {
                // --- Losing Game Logic ---
                // The rules for generating a losing panel depend on the overall state of the ticket.
                if (ticket.WinPrize.Value > 0)
                {
                    // Rule: If the ticket is already a winner, this losing panel must have no near-misses
                    // to avoid confusing the player.
                    FillWithNoNearMisses(gridItems, itemPool, random);
                }
                else
                {
                    // Rule: This is a completely losing ticket, so a more engaging panel can be generated.
                    if (ItemsToMatch <= 2)
                    {
                        // For simpler games (e.g., Match 2), a losing panel must be unambiguous.
                        FillWithNoNearMisses(gridItems, itemPool, random);
                    }
                    else
                    {
                        // For "Match 3" or higher, near-misses are allowed.
                        // As a special rule, we avoid a near-miss of the game's highest value prize.
                        // Step 2b: Get the highest value item from the subclass for this specific rule.
                        var highestValueItem = GetHighestValueItem(project);
                        var filteredItemPool = highestValueItem == -1 ? itemPool : itemPool.Where(s => s != highestValueItem).ToList();

                        // The new method internally handles possibility checks, simplifying this call.
                        FillWithWeightedMultipleNearMisses(gridItems, filteredItemPool.Any() ? filteredItemPool : itemPool, ItemsToMatch, random, project.Settings.LosingTicketNearMissWeighting, project.Settings.FavourHighValueNearMisses, project);
                    }
                }
            }

            // Final Step: Populate the play data and add it to the ticket.
            playData.GeneratedSymbolIds.AddRange(gridItems);
            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Protected Helper Methods

        /// <summary>
        /// Fills a grid (or part of a grid) with items, allowing for "near-misses" where an item can appear
        /// up to (requiredMatches - 1) times, but never enough to form a winning combination.
        /// </summary>
        /// <param name="grid">The integer array representing the grid to be filled.</param>
        /// <param name="itemPool">The list of item IDs available to use.</param>
        /// <param name="requiredMatches">The number of matches required for a win.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        /// <exception cref="InvalidOperationException">Thrown if a non-winning panel cannot be generated due to insufficient unique items.</exception>
        protected void FillWithNearMisses(int[] grid, List<int> itemPool, int requiredMatches, Random random)
        {
            // Safety check for the logic.
            if (requiredMatches < 2) requiredMatches = 2;

            // Validate if a non-winning panel is possible with the given constraints.
            // This prevents an infinite loop if item variety is too low for the grid size and match rule.
            if (itemPool.Distinct().Count() < grid.Length && (requiredMatches - 1) * itemPool.Distinct().Count() < grid.Length)
            {
                throw new InvalidOperationException($"Cannot generate a non-winning panel for {ModuleName}. The number of unique items ({itemPool.Distinct().Count()}) is too low for the grid size ({grid.Length}) and the 'Match {requiredMatches}' rule.");
            }

            var itemCounts = new Dictionary<int, int>();
            for (int i = 0; i < grid.Length; i++)
            {
                // Find all items that can still be placed without causing an accidental win.
                var eligibleItems = itemPool.Where(s => !itemCounts.ContainsKey(s) || itemCounts[s] < (requiredMatches - 1)).ToList();

                // If we run out of eligible items (a rare edge case), reset the pool and counts to avoid getting stuck.
                if (!eligibleItems.Any())
                {
                    Shuffle(itemPool, random);
                    eligibleItems = new List<int>(itemPool);
                    itemCounts.Clear();
                }

                int itemToAdd = eligibleItems[random.Next(eligibleItems.Count)];
                grid[i] = itemToAdd;

                // Update the count for the item that was just added.
                if (itemCounts.ContainsKey(itemToAdd)) itemCounts[itemToAdd]++;
                else itemCounts.Add(itemToAdd, 1);
            }
        }

        /// <summary>
        /// Fills a grid with unique items, ensuring no duplicates and therefore no near-misses.
        /// This creates the least confusing type of losing panel.
        /// </summary>
        /// <param name="grid">The integer array representing the grid to be filled.</param>
        /// <param name="itemPool">The list of item IDs available to use.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        /// <exception cref="InvalidOperationException">Thrown if a panel with no near-misses cannot be generated due to insufficient unique items.</exception>
        protected void FillWithNoNearMisses(int[] grid, List<int> itemPool, Random random)
        {
            // A panel with no near-misses requires at least as many unique items as there are slots in the grid.
            if (itemPool.Distinct().Count() < grid.Length)
            {
                throw new InvalidOperationException($"Cannot generate a panel for {ModuleName} with no near misses. The number of unique items ({itemPool.Distinct().Count()}) is less than the number of required slots ({grid.Length}).");
            }

            // Get a shuffled, distinct list of items and take the first 'grid.Length' items.
            var shuffledItems = itemPool.Distinct().OrderBy(s => random.Next()).ToList();
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = shuffledItems[i];
            }
        }

        /// <summary>
        /// Fills a grid or part of a grid with decoy items. This method is virtual so it can be overridden
        /// in subclasses if more complex decoy generation rules are required.
        /// By default, it ensures no accidental wins are created in the decoy area.
        /// </summary>
        /// <param name="grid">The integer array for the decoy part of the grid.</param>
        /// <param name="decoyItemPool">The pool of available decoy items.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        protected virtual void FillWithDecoys(int[] grid, List<int> decoyItemPool, Random random)
        {
            FillWithNoNearMisses(grid, decoyItemPool, random);
        }

        /// <summary>
        /// Shuffles an integer array in-place using the Fisher-Yates algorithm, which provides
        /// an unbiased permutation of the array's elements.
        /// </summary>
        /// <param name="array">The array to shuffle.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        protected void Shuffle(int[] array, Random random)
        {
            int n = array.Length;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                // A modern C# tuple swap is a concise way to exchange elements.
                (array[k], array[n]) = (array[n], array[k]);
            }
        }

        /// <summary>
        /// Fills a grid with a losing combination of items that includes a random number of "near-miss" sets.
        /// This is the core method for creating psychologically engaging losing tickets.
        /// </summary>
        /// <remarks>
        /// This complex method orchestrates several game design features:
        /// 1.  **Quantity Control:** It determines how many near-miss sets to create based on the <paramref name="weighting"/> parameter (Low, Balanced, High).
        /// 2.  **Quality Control:** If the <paramref name="favourHighValue"/> flag is true, it gives a 50% chance to make one of the near-misses a high-value item, increasing player excitement. This now correctly includes the top prize.
        /// 3.  **Safety:** It guarantees that the generated panel is never an accidental winner by carefully managing the pool of items used for near misses versus those used for filling the remaining slots.
        /// 4.  **Robustness:** It includes fallback logic to handle edge cases where the variety of available items is too low to meet the desired near-miss criteria, preventing crashes.
        /// </remarks>
        /// <param name="grid">The integer array representing the grid to be filled.</param>
        /// <param name="itemPool">The list of item IDs available to use. For losing tickets, this pool should already exclude the absolute highest-value prize to avoid every ticket being a jackpot near-miss.</param>
        /// <param name="requiredMatches">The number of matches required for a win (e.g., 3 for a "Match 3" game).</param>
        /// <param name="random">The shared <see cref="Random"/> instance for all random selections.</param>
        /// <param name="weighting">The user-selected weighting preference which controls the quantity of near-miss sets.</param>
        /// <param name="favourHighValue">A boolean indicating whether to probabilistically favour a high-value item for one of the near-miss sets.</param>
        /// <param name="project">The entire project context, needed to look up the values of prizes or symbols.</param>
        protected void FillWithWeightedMultipleNearMisses(int[] grid, List<int> itemPool, int requiredMatches, Random random, NearMissWeighting weighting, bool favourHighValue, ScratchCardProject project)
        {
            // --- Step 1: Pre-computation and Sanity Checks ---

            // A "near-miss" is defined as having one less than the required number of matches.
            int slotsPerNearMiss = requiredMatches - 1;

            // Sanity check: If a game requires matching 1 or fewer items, the concept of a near-miss is not applicable.
            // In this case, we must generate a panel with no near-misses at all to ensure a clear losing ticket.
            if (slotsPerNearMiss <= 0)
            {
                FillWithNoNearMisses(grid, itemPool, random);
                return;
            }

            // Determine the absolute maximum number of near-miss sets that could possibly fit on the grid.
            // This is limited by two factors: the physical space on the grid and the number of unique items available to use.
            int maxPossibleBySlots = grid.Length / slotsPerNearMiss;
            int maxPossibleByItems = itemPool.Distinct().Count();
            int maxPossibleNearMisses = Math.Min(maxPossibleBySlots, maxPossibleByItems);

            // If the configuration makes it impossible to even create one near-miss (e.g., a 2x2 grid in a Match-3 game),
            // we fall back to the older, simpler near-miss logic which can handle smaller spaces.
            if (maxPossibleNearMisses == 0)
            {
                FillWithNearMisses(grid, itemPool, requiredMatches, random);
                return;
            }

            // --- Step 2: Determine the Quantity of Near-Misses ---

            // Based on the user's preference (Low, Balanced, High), create a "weighted list" from which we will randomly pick.
            // This list determines how many near-miss sets we will attempt to create.
            var weightedList = new List<int>();
            switch (weighting)
            {
                case NearMissWeighting.Low:
                    // This loop creates a list that is heavily skewed towards lower numbers.
                    // e.g., for max=3, the list becomes [1, 1, 1, 2, 2, 3], making '1' the most likely outcome.
                    for (int i = 1; i <= maxPossibleNearMisses; i++) { for (int j = 0; j < (maxPossibleNearMisses - i + 1); j++) weightedList.Add(i); }
                    break;
                case NearMissWeighting.Balanced:
                    // This creates a simple list where each possible number of near-misses has an equal chance.
                    // e.g., for max=3, the list is [1, 2, 3].
                    for (int i = 1; i <= maxPossibleNearMisses; i++) weightedList.Add(i);
                    break;
                case NearMissWeighting.High:
                default:
                    // This loop creates a list skewed towards higher numbers.
                    // e.g., for max=3, list is [1, 2, 2, 3, 3, 3], making '3' the most likely outcome.
                    for (int i = 1; i <= maxPossibleNearMisses; i++) { for (int j = 0; j < i; j++) weightedList.Add(i); }
                    break;
            }

            // Randomly select the target number of near-misses to generate from our weighted list.
            int numNearMissesToCreate = weightedList.Any() ? weightedList[random.Next(weightedList.Count)] : 0;

            // Initialise lists to hold the items that will be placed on the grid.
            var currentGridItems = new List<int>();
            var availableItems = itemPool.Distinct().OrderBy(x => random.Next()).ToList(); // A shuffled list of all unique items we can use.
            var nearMissItems = new List<int>();

            // --- Step 3: Select the Near-Miss Items (Quality Control) ---

            // Check if the user wants to favour high-value near misses and if it's possible to create any.
            // This now includes a 50% chance, making the feature probabilistic rather than a guarantee on every ticket.
            // Picks a random number between 0 and 1, so a 50% chance.
            // To Make it rarer, use 10 for a 10% chance or 4 for 25% chance.
            // Note: This logic assumes that the item pool has already excluded the absolute highest-value prize to avoid every ticket being a jackpot near-miss.
            if (favourHighValue && numNearMissesToCreate > 0 && random.Next(4) == 0) // 25% chance
            {
                // 3a. Identify a pool of "high-value" items. This logic now correctly includes the top prize.
                List<int> highValueItemPool;

                // The method of determining an item's "value" differs between prize-based and symbol-based games.
                if (this is MatchPrizesInGridGame || this is MatchPrizesInRowGame)
                {
                    // For prize games, the item ID is the index in the PrizeTiers list. We can sort directly by prize value.
                    highValueItemPool = itemPool
                        .OrderByDescending(pIndex => project.PrizeTiers.ElementAtOrDefault(pIndex)?.Value ?? 0)
                        .Take(Math.Max(1, itemPool.Count / 4)) // Take the top 25% of prizes.
                        .ToList();
                }
                else // This block handles symbol-based games (e.g., MatchSymbolsInGridGame).
                {
                    // For symbol games, we need a helper function to look up the prize value associated with a symbol's TextCode.
                    int GetValueForSymbol(int symbolId)
                    {
                        var symbol = project.AvailableSymbols.FirstOrDefault(s => s.Id == symbolId);
                        if (symbol == null) return 0;
                        var prize = project.PrizeTiers.FirstOrDefault(p => p.TextCode == symbol.Name && !p.IsOnlinePrize);
                        return prize?.Value ?? 0;
                    }
                    // We use the helper function to sort the symbols by their associated prize value.
                    highValueItemPool = itemPool
                        .OrderByDescending(GetValueForSymbol)
                        .Take(Math.Max(1, itemPool.Count / 4)) // Take the top 25% of symbols.
                        .ToList();
                }

                if (highValueItemPool.Any())
                {
                    // If we found high-value items, pick one at random to be our guaranteed high-value near-miss.
                    int highValueNearMiss = highValueItemPool[random.Next(highValueItemPool.Count)];
                    nearMissItems.Add(highValueNearMiss);
                    availableItems.Remove(highValueNearMiss); // It's crucial to remove it from the general pool to avoid picking it again.
                }
            }

            // 3b. Select the remaining near-miss items randomly from the rest of the available pool.
            // The Take() method gracefully handles cases where we don't need any more items (e.g., if we already picked one high-value item and numNearMissesToCreate was 1).
            nearMissItems.AddRange(availableItems.Take(numNearMissesToCreate - nearMissItems.Count));

            // --- Step 4: Populate the Grid with Items ---

            // 4a. Add the selected near-miss items to the grid, each appearing (requiredMatches - 1) times.
            foreach (var item in nearMissItems)
            {
                for (int i = 0; i < slotsPerNearMiss; i++)
                {
                    currentGridItems.Add(item);
                }
            }

            // 4b. Fill the remaining empty slots with "decoy" items.
            // A decoy item is any item that was not used for a near-miss.
            var decoyItems = itemPool.Distinct().Except(nearMissItems).OrderBy(x => random.Next()).ToList();
            int remainingSlots = grid.Length - currentGridItems.Count;

            // It's possible, in rare configurations, that we don't have enough *unique* decoy items to fill the rest of the grid.
            if (decoyItems.Count < remainingSlots)
            {
                // If we lack unique decoys, we fall back to the older FillWithNearMisses method.
                // This method can safely fill the remaining space with non-unique decoys without creating an accidental win.
                var remainingGridPart = new int[remainingSlots];
                var safeDecoyPool = itemPool.Where(i => !nearMissItems.Contains(i)).ToList();
                FillWithNearMisses(remainingGridPart, safeDecoyPool.Any() ? safeDecoyPool : itemPool, requiredMatches, random);
                currentGridItems.AddRange(remainingGridPart);
            }
            else
            {
                // In the ideal case, we have enough unique decoys to fill the remaining slots.
                currentGridItems.AddRange(decoyItems.Take(remainingSlots));
            }

            // --- Step 5: Finalize the Grid ---

            // Copy the generated list of items into the final grid array.
            currentGridItems.ToArray().CopyTo(grid, 0);
            // Shuffle the entire grid to randomise the positions of all items, hiding the generation pattern.
            Shuffle(grid, random);
        }

        /// <summary>
        /// Shuffles an integer list in-place using the Fisher-Yates algorithm.
        /// </summary>
        /// <param name="list">The list to shuffle.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        protected void Shuffle(List<int> list, Random random)
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
    }

    #endregion
}