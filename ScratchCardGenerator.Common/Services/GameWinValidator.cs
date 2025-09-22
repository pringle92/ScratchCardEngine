#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using ScratchCardGenerator.Common.Models;
using System.Collections.Generic;
using System.Linq;

#endregion

namespace ScratchCardGenerator.Common.Services
{
    /// <summary>
    /// Provides centralised logic for validating if a given winning ticket legitimately satisfies the rules of its winning game.
    /// This service acts as a single source of truth for win validation, ensuring that the rules applied during generation
    /// are the same as those used for verification. This is a key component for ensuring data integrity.
    /// </summary>
    public class GameWinValidator
    {
        #region Public Methods

        /// <summary>
        /// Validates a single ticket to ensure its game data corresponds to a legitimate win according to the rules of the winning game module.
        /// </summary>
        /// <param name="ticket">The ticket to validate.</param>
        /// <param name="project">The project context, which contains the definitions and rules for all game modules.</param>
        /// <param name="ticketIndexForError">The index of the ticket, used for creating clear and specific error messages.</param>
        /// <param name="errorMessages">A list to which any validation error messages will be added.</param>
        /// <returns>True if the ticket is a valid winner or a loser; otherwise, false if it is an invalid winner.</returns>
        public bool ValidateTicketWin(Ticket ticket, ScratchCardProject project, int ticketIndexForError, ref List<string> errorMessages)
        {
            // If the ticket is not marked as a winner, it is considered valid in this context.
            // This validator is only concerned with the integrity of winning tickets.
            if (ticket.WinPrize?.Value <= 0)
            {
                return true;
            }

            // Find the specific GamePlayData entry that is marked as the winner for this ticket.
            var winningGameData = ticket.GameData.FirstOrDefault(gd => gd.PrizeTierIndex >= 0);
            if (winningGameData == null)
            {
                // This is a critical error: the ticket is marked as a winner overall, but no individual game claims the win.
                string gameDataDump = string.Join("; ", ticket.GameData.Select(gd => $"Game {gd.GameNumber}: PrizeTierIndex={gd.PrizeTierIndex}"));
                errorMessages.Add($"Ticket {ticketIndexForError} (Win Value: {ticket.WinPrize.Value:C}) failed validation. Could not find a winning game entry (PrizeTierIndex >= 0). GameData Dump: [{gameDataDump}]");
                return false;
            }

            // Find the GameModule definition that corresponds to the winning game number.
            var winningGameModule = project.Layout.GameModules.FirstOrDefault(m => m.GameNumber == winningGameData.GameNumber);
            if (winningGameModule == null)
            {
                // This indicates a data corruption or configuration issue where the ticket data refers to a non-existent game.
                errorMessages.Add($"Ticket {ticketIndexForError} has an invalid winning game number: {winningGameData.GameNumber}. The GameModule could not be found.");
                return false;
            }

            // Route to the specific validation logic based on the type of game module.
            return IsGameLogicValid(winningGameModule, winningGameData, ticketIndexForError, ref errorMessages);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Contains the specific rule-checking logic for each type of game module. It uses a switch statement
        /// to apply the correct validation rules based on the concrete type of the GameModule.
        /// </summary>
        /// <param name="game">The game module defining the rules for the win.</param>
        /// <param name="gameData">The play data for the winning game on the ticket.</param>
        /// <param name="ticketIndex">The index of the ticket for error reporting.</param>
        /// <param name="errors">The list to which error messages will be added.</param>
        /// <returns>True if the game data represents a valid win for the given game type; otherwise, false.</returns>
        private bool IsGameLogicValid(GameModule game, GamePlayData gameData, int ticketIndex, ref List<string> errors)
        {
            switch (game)
            {
                case FindWinningSymbolGame fwsGame:
                    // Rule: The panel must contain the pre-defined winning symbol.
                    if (!gameData.GeneratedSymbolIds.Contains(fwsGame.WinningSymbolId))
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) won but does not contain the winning symbol (ID: {fwsGame.WinningSymbolId}).");
                        return false;
                    }
                    break;

                case MatchSymbolToPrizeGame mstpGame:
                    // Rule: The panel must contain the pre-defined winning symbol for this game.
                    if (!gameData.GeneratedSymbolIds.Contains(mstpGame.WinningSymbolId))
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) won but does not contain the winning symbol (ID: {mstpGame.WinningSymbolId}).");
                        return false;
                    }
                    break;

                case MatchSymbolsInRowGame msirGame:
                    // Rule: At least one row must contain the required number of matching symbols.
                    bool hasWinningRow = false;
                    for (int r = 0; r < msirGame.NumberOfRows; r++)
                    {
                        // Group symbols by their ID for the current row and check if any group's count meets the win condition.
                        if (gameData.GeneratedSymbolIds.Skip(r * msirGame.SymbolsPerRow).Take(msirGame.SymbolsPerRow).GroupBy(s => s).Any(g => g.Count() >= msirGame.SymbolsToMatchInRow))
                        {
                            hasWinningRow = true;
                            break; // Found a winning row, no need to check further.
                        }
                    }
                    if (!hasWinningRow)
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) is marked as a winner, but no winning row was found.");
                        return false;
                    }
                    break;

                case MatchPrizesInRowGame mpirGame:
                    // Rule: At least one row must contain the required number of matching prize values.
                    bool hasPrizeWinningRow = false;
                    for (int r = 0; r < mpirGame.NumberOfRows; r++)
                    {
                        // Logic is identical to the symbol version, but operates on prize indices.
                        if (gameData.GeneratedSymbolIds.Skip(r * mpirGame.PrizesPerRow).Take(mpirGame.PrizesPerRow).GroupBy(p => p).Any(g => g.Count() >= mpirGame.PrizesToMatchInRow))
                        {
                            hasPrizeWinningRow = true;
                            break;
                        }
                    }
                    if (!hasPrizeWinningRow)
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) is marked as a winner, but no winning row was found.");
                        return false;
                    }
                    break;

                case MatchSymbolsInGridGame msigGame:
                    // Rule: The entire grid must contain at least one symbol that appears the required number of times.
                    if (!gameData.GeneratedSymbolIds.GroupBy(s => s).Any(g => g.Count() >= msigGame.SymbolsToMatch))
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) is marked as a winner, but no symbol appeared {msigGame.SymbolsToMatch} times.");
                        return false;
                    }
                    break;

                case MatchPrizesInGridGame mpigGame:
                    // Rule: The entire grid must contain at least one prize value that appears the required number of times.
                    if (!gameData.GeneratedSymbolIds.GroupBy(p => p).Any(g => g.Count() >= mpigGame.PrizesToMatch))
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) is marked as a winner, but no prize appeared {mpigGame.PrizesToMatch} times.");
                        return false;
                    }
                    break;

                case ChristmasTreeGame ctGame:
                    // Rule: The panel must contain the pre-defined winning symbol.
                    if (!gameData.GeneratedSymbolIds.Contains(ctGame.WinningSymbolId))
                    {
                        errors.Add($"Ticket {ticketIndex} ({game.GetType().Name}) won but does not contain the winning symbol (ID: {ctGame.WinningSymbolId}).");
                        return false;
                    }
                    break;

                case OnlineBonusGame:
                    // An Online Bonus game has no playable symbols to validate. Its win is determined solely by the
                    // prize tier being assigned to it. Therefore, if the ticket points to this game as the winner, it's valid by definition.
                    break;

                default:
                    // This case handles any future game types that might be added but not yet implemented in the validator.
                    errors.Add($"Ticket {ticketIndex} won on an unknown or un-implemented game type: '{game.GetType().Name}'. Validation cannot proceed for this ticket.");
                    return false;
            }

            // If we've passed all the checks for the specific game type, the ticket is valid.
            return true;
        }

        #endregion
    }
}