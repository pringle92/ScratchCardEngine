#region Usings

using System;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Row Game Module Base

    /// <summary>
    /// Provides an abstract base class for game modules where play occurs in multiple independent rows,
    /// and a win requires matching a certain number of items within one of those rows.
    /// </summary>
    /// <remarks>
    /// This class extends <see cref="GridGameModuleBase"/> to inherit its helper methods for panel generation (e.g., FillWithNearMisses),
    /// but it overrides the primary <see cref="GeneratePlayData"/> "template method" to implement its own row-centric algorithm.
    /// It iterates through each row, generating it as either a winner or a loser, thus encapsulating the logic common to all row-based games.
    /// </remarks>
    public abstract class RowGameModuleBase : GridGameModuleBase
    {
        #region Properties

        /// <summary>
        /// Gets or sets the total number of independent rows in the game.
        /// </summary>
        public int NumberOfRows { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of items (e.g., symbols or prizes) that appear in each row.
        /// </summary>
        public int ItemsPerRow { get; set; } = 3;

        /// <summary>
        /// Gets or sets the number of identical items required within a single row to constitute a win.
        /// </summary>
        /// <remarks>
        /// The 'new' keyword is used here to deliberately hide the base class's 'ItemsToMatch' property.
        /// This allows concrete subclasses to provide a more contextually appropriate property name (e.g., "SymbolsToMatchInRow")
        /// while still mapping to this underlying logic.
        /// </remarks>
        public new int ItemsToMatch { get; set; } = 3;

        #endregion

        #region Gameplay Generation (Template Method Override)

        /// <summary>
        /// Overrides the base generation algorithm to provide a row-specific implementation.
        /// It constructs the game panel by generating each row independently, randomly designating one row as the winner if required.
        /// </summary>
        /// <param name="ticket">The parent ticket object to which the generated data will be added.</param>
        /// <param name="isWinningGame">A boolean indicating if this module should produce a winning outcome.</param>
        /// <param name="winTier">The prize tier to be awarded if this is a winning game.</param>
        /// <param name="project">The project context for accessing available symbols and prizes.</param>
        /// <param name="random">A shared <see cref="Random"/> instance for generating random outcomes.</param>
        public override void GeneratePlayData(Ticket ticket, bool isWinningGame, PrizeTier winTier, ScratchCardProject project, Random random)
        {
            var playData = new GamePlayData { GameNumber = this.GameNumber };
            var allItems = new List<int>();

            // If this is a winning game, randomly select which row will contain the winning combination.
            // A value of -1 indicates no row will be a winner.
            int winningRowIndex = isWinningGame ? random.Next(0, NumberOfRows) : -1;

            // Generate each row of the game panel independently.
            for (int r = 0; r < NumberOfRows; r++)
            {
                bool isRowTheWinner = (r == winningRowIndex);
                allItems.AddRange(GenerateSingleRow(isRowTheWinner, winTier, project, ticket, random));
            }

            // Add the complete list of items from all rows to the ticket's play data.
            playData.GeneratedSymbolIds.AddRange(allItems);

            if (isWinningGame)
            {
                playData.PrizeTierIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == winTier.Value && p.IsOnlinePrize == winTier.IsOnlinePrize);
            }

            ticket.GameData.Add(playData);
        }

        #endregion

        #region Private Row Generation Logic

        /// <summary>
        /// Generates the item data for a single row, which can be either a winning or a losing combination.
        /// This method encapsulates the logic for creating one self-contained row.
        /// </summary>
        /// <param name="isWinningRow">A boolean indicating if this specific row should be a winning combination.</param>
        /// <param name="winTier">The prize tier to award if this is a winning row.</param>
        /// <param name="project">The project context.</param>
        /// <param name="ticket">The parent ticket, used to determine near-miss rules for losing rows.</param>
        /// <param name="random">The shared <see cref="Random"/> instance.</param>
        /// <returns>A list of item IDs representing the generated row.</returns>
        private List<int> GenerateSingleRow(bool isWinningRow, PrizeTier winTier, ScratchCardProject project, Ticket ticket, Random random)
        {
            var rowItems = new int[ItemsPerRow];

            // Get the item pool (symbols or prizes) from the concrete subclass implementation.
            var itemPool = GetItemPool(project);

            if (isWinningRow)
            {
                // --- Winning Row Logic ---
                int winningItem = GetWinningItem(winTier, project, random);
                var decoyItemPool = GetDecoyItemPool(winningItem, itemPool, project);

                // Downgrade to a losing row if no decoy items are available to fill the remaining slots.
                if (!decoyItemPool.Any())
                {
                    isWinningRow = false;
                }
                else
                {
                    // Place the winning items, fill the rest with decoys, and shuffle.
                    for (int i = 0; i < ItemsToMatch; i++)
                    {
                        rowItems[i] = winningItem;
                    }
                    var decoyPart = new int[ItemsPerRow - ItemsToMatch];
                    FillWithNoNearMisses(decoyPart, decoyItemPool, random); // Ensure no accidental wins among decoys.

                    Array.Copy(decoyPart, 0, rowItems, ItemsToMatch, decoyPart.Length);
                    Shuffle(rowItems, random);
                }
            }

            // Note: This block can be entered either if the row was initially a loser,
            // or if a winning row was downgraded due to a lack of decoys.
            if (!isWinningRow)
            {
                // --- Losing Row Logic ---
                // The logic for generating a losing row is identical to the grid-based game,
                // but applied here on a smaller, row-sized scale.
                if (ticket.WinPrize.Value > 0)
                {
                    // If the ticket is already a winner elsewhere, this row must have no near-misses.
                    FillWithNoNearMisses(rowItems, itemPool, random);
                }
                else
                {
                    if (ItemsToMatch <= 2)
                    {
                        // Match-2 style games must also have no near-misses on a losing panel.
                        FillWithNoNearMisses(rowItems, itemPool, random);
                    }
                    else
                    {
                        // On a completely losing ticket, near-misses are allowed for excitement,
                        // but not for the top prize.
                        var highestValueItem = GetHighestValueItem(project);
                        var filteredItemPool = highestValueItem == -1 ? itemPool : itemPool.Where(s => s != highestValueItem).ToList();

                        // We now call the new weighted, multiple near-miss method from the base class.
                        // This simplifies the logic here as the new method handles all possibility checks internally.
                        FillWithWeightedMultipleNearMisses(rowItems, filteredItemPool.Any() ? filteredItemPool : itemPool, ItemsToMatch, random, project.Settings.LosingTicketNearMissWeighting, project.Settings.FavourHighValueNearMisses, project);

                        FillWithNearMisses(rowItems, filteredItemPool.Any() ? filteredItemPool : itemPool, ItemsToMatch, random);

                    }
                }
            }
            return rowItems.ToList();
        }

        #endregion
    }

    #endregion
}