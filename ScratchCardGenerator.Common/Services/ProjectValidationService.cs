#region Usings

using ScratchCardGenerator.Common.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows; // Required for the Rect structure used in overlap detection.

#endregion

namespace ScratchCardGenerator.Common.Services
{
    #region Project Validation Service

    /// <summary>
    /// Provides services for performing logical validation on a ScratchCardProject to identify
    /// potential configuration errors or inconsistencies before generation is attempted.
    /// </summary>
    public class ProjectValidationService
    {
        #region Public Methods

        /// <summary>
        /// Validates the entire scratch card project against a set of predefined rules.
        /// </summary>
        /// <param name="project">The project to validate.</param>
        /// <returns>A list of <see cref="ValidationIssue"/> objects representing any problems found.</returns>
        public List<ValidationIssue> Validate(ScratchCardProject project)
        {
            var issues = new List<ValidationIssue>();
            if (project == null) return issues;

            // --- Run all validation checks ---
            CheckForDuplicateIds(project, issues);
            CheckForSufficientUniqueSymbols(project, issues);
            CheckSymbolToPrizeLinks(project, issues);
            CheckPrizeTierConfiguration(project, issues);
            CheckSecurityConfiguration(project, issues);
            CheckForCompatibleModules(project, issues);

            // NEW: Add the new validation checks.
            CheckPrizeValueAgainstTicketPrice(project, issues);
            CheckForZeroCountPrizes(project, issues);
            CheckForModuleOverlap(project, issues);

            return issues;
        }

        #endregion

        #region Private Validation Checks

        /// <summary>
        /// Checks for duplicate IDs within the symbol and prize tier collections.
        /// </summary>
        private void CheckForDuplicateIds(ScratchCardProject project, List<ValidationIssue> issues)
        {
            var duplicateSymbolIds = project.AvailableSymbols.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key.ToString());
            if (duplicateSymbolIds.Any())
            {
                issues.Add(new ValidationIssue($"Critical data error: Duplicate Symbol IDs found: {string.Join(", ", duplicateSymbolIds)}. Please ensure all Symbol IDs are unique.", IssueSeverity.Error, project.AvailableSymbols));
            }

            var duplicateNumericSymbolIds = project.NumericSymbols.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key.ToString());
            if (duplicateNumericSymbolIds.Any())
            {
                issues.Add(new ValidationIssue($"Critical data error: Duplicate Game Symbol IDs found: {string.Join(", ", duplicateNumericSymbolIds)}. Please ensure all Game Symbol IDs are unique.", IssueSeverity.Error, project.NumericSymbols));
            }

            var duplicatePrizeIds = project.PrizeTiers.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key.ToString());
            if (duplicatePrizeIds.Any())
            {
                issues.Add(new ValidationIssue($"Critical data error: Duplicate Prize Tier IDs found: {string.Join(", ", duplicatePrizeIds)}. Please ensure all Prize Tier IDs are unique.", IssueSeverity.Error, project.PrizeTiers));
            }
        }

        /// <summary>
        /// Checks if there are enough unique symbols defined for the requirements of each game module.
        /// </summary>
        private void CheckForSufficientUniqueSymbols(ScratchCardProject project, List<ValidationIssue> issues)
        {
            if (project.AvailableSymbols == null) return;
            int uniqueSymbolCount = project.AvailableSymbols.Select(s => s.Id).Distinct().Count();

            foreach (var module in project.Layout.GameModules)
            {
                int requiredSymbols = 0;
                if (module is GridGameModuleBase gridGame)
                {
                    requiredSymbols = gridGame.Rows * gridGame.Columns;
                }
                else if (module is RowGameModuleBase rowGame)
                {
                    requiredSymbols = rowGame.ItemsPerRow;
                }

                if (requiredSymbols > 0 && uniqueSymbolCount < requiredSymbols)
                {
                    issues.Add(new ValidationIssue(
                        $"'{module.ModuleName}' (Game {module.GameNumber}) requires at least {requiredSymbols} unique symbols for a non-winning panel, but only {uniqueSymbolCount} are available.",
                        IssueSeverity.Error,
                        module));
                }
            }
        }

        /// <summary>
        /// Checks that every 'Match Symbol to Prize' game is linked to a valid prize tier.
        /// </summary>
        private void CheckSymbolToPrizeLinks(ScratchCardProject project, List<ValidationIssue> issues)
        {
            foreach (var module in project.Layout.GameModules.OfType<MatchSymbolToPrizeGame>())
            {
                var symbol = project.NumericSymbols.FirstOrDefault(s => s.Id == module.WinningSymbolId);
                if (symbol == null)
                {
                    issues.Add(new ValidationIssue($"'{module.ModuleName}' (Game {module.GameNumber}) has an invalid 'Winning Symbol' selected.", IssueSeverity.Error, module));
                    continue;
                }

                var prize = project.PrizeTiers.FirstOrDefault(p => p.TextCode == symbol.Name && !p.IsOnlinePrize);
                if (prize == null)
                {
                    issues.Add(new ValidationIssue($"'{module.ModuleName}' (Game {module.GameNumber}) is linked to symbol '{symbol.DisplayText}', but no prize tier has the matching TextCode '{symbol.Name}'.", IssueSeverity.Warning, module));
                }
            }
        }

        /// <summary>
        /// Checks for common configuration issues in the prize tiers, like missing barcodes.
        /// </summary>
        private void CheckPrizeTierConfiguration(ScratchCardProject project, List<ValidationIssue> issues)
        {
            if (project.Settings.IsPoundlandGame) return;

            foreach (var prize in project.PrizeTiers.Where(p => p.Value > 0 && !p.IsOnlinePrize && string.IsNullOrWhiteSpace(p.Barcode)))
            {
                issues.Add(new ValidationIssue($"The '{prize.DisplayText}' prize tier has no barcode assigned. This may cause issues in downstream systems.", IssueSeverity.Warning, prize));
            }
        }

        /// <summary>
        /// Warns if no security files are specified, and raises an error for Poundland games which require them.
        /// </summary>
        private void CheckSecurityConfiguration(ScratchCardProject project, List<ValidationIssue> issues)
        {
            if (project.Settings.IsPoundlandGame)
            {
                if (string.IsNullOrWhiteSpace(project.Security.ThreeDigitCodeFilePath) || string.IsNullOrWhiteSpace(project.Security.SevenDigitCodeFilePath))
                {
                    issues.Add(new ValidationIssue("Poundland games require both a 3-Digit and a 7-Digit security file to be specified.", IssueSeverity.Error, project.Security));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(project.Security.SixDigitCodeFilePath) &&
                    (string.IsNullOrWhiteSpace(project.Security.ThreeDigitCodeFilePath) || string.IsNullOrWhiteSpace(project.Security.SevenDigitCodeFilePath)))
                {
                    issues.Add(new ValidationIssue("No security code files are specified. You will not be able to generate combined files.", IssueSeverity.Warning, project.Security));
                }
            }
        }

        /// <summary>
        /// Checks that at least one compatible game module exists for every type of active prize (online/offline).
        /// </summary>
        private void CheckForCompatibleModules(ScratchCardProject project, List<ValidationIssue> issues)
        {
            bool hasActiveOnlinePrizes = project.PrizeTiers.Any(p => p.IsOnlinePrize && p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0));
            bool hasOnlineModule = project.Layout.GameModules.OfType<OnlineBonusGame>().Any();

            if (hasActiveOnlinePrizes && !hasOnlineModule)
            {
                issues.Add(new ValidationIssue("The project has active online prizes, but no 'Online Bonus (QR)' module exists on the card layout.", IssueSeverity.Error, project.Layout));
            }

            var offlinePrizes = project.PrizeTiers.Where(p => !p.IsOnlinePrize && p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0)).ToList();
            if (offlinePrizes.Any())
            {
                var compatibleOfflineModules = project.Layout.GameModules.Where(m => !(m is OnlineBonusGame)).ToList();
                if (!compatibleOfflineModules.Any())
                {
                    issues.Add(new ValidationIssue("The project has active offline prizes, but no game modules capable of awarding them (e.g., Match Symbols, Find Symbol).", IssueSeverity.Error, project.Layout));
                }
            }
        }

        /// <summary>
        /// NEW: Checks for any prize tiers valued below the ticket's sale price.
        /// </summary>
        private void CheckPrizeValueAgainstTicketPrice(ScratchCardProject project, List<ValidationIssue> issues)
        {
            if (project.Settings.TicketSalePrice <= 0) return;

            foreach (var prize in project.PrizeTiers)
            {
                if (prize.Value > 0 && prize.Value < project.Settings.TicketSalePrice)
                {
                    issues.Add(new ValidationIssue(
                        $"Prize '{prize.DisplayText}' has a value ({prize.Value:C}) that is less than the ticket sale price ({project.Settings.TicketSalePrice:C}).",
                        IssueSeverity.Warning,
                        prize));
                }
            }
        }

        /// <summary>
        /// NEW: Checks for prize tiers that are defined but can never be won because their winner counts are zero.
        /// </summary>
        private void CheckForZeroCountPrizes(ScratchCardProject project, List<ValidationIssue> issues)
        {
            foreach (var prize in project.PrizeTiers)
            {
                if (prize.Value > 0 && prize.LvwWinnerCount == 0 && prize.HvwWinnerCount == 0)
                {
                    issues.Add(new ValidationIssue(
                        $"Prize '{prize.DisplayText}' is defined but can never be won as its LVW and HVW counts are both zero.",
                        IssueSeverity.Warning,
                        prize));
                }
            }
        }

        /// <summary>
        /// NEW: Checks if any game modules are visually overlapping on the designer canvas.
        /// </summary>
        private void CheckForModuleOverlap(ScratchCardProject project, List<ValidationIssue> issues)
        {
            var modules = project.Layout.GameModules.ToList();
            // Use a nested loop to compare every unique pair of modules.
            for (int i = 0; i < modules.Count; i++)
            {
                var rectA = new Rect(modules[i].Position, modules[i].Size);
                for (int j = i + 1; j < modules.Count; j++)
                {
                    var rectB = new Rect(modules[j].Position, modules[j].Size);

                    // The IntersectsWith method efficiently checks for any overlap between two rectangles.
                    if (rectA.IntersectsWith(rectB))
                    {
                        issues.Add(new ValidationIssue(
                            $"'{modules[i].ModuleName}' (Game {modules[i].GameNumber}) overlaps with '{modules[j].ModuleName}' (Game {modules[j].GameNumber}) on the canvas.",
                            IssueSeverity.Warning,
                            modules[i])); // Report the issue against the first module in the pair.
                    }
                }
            }
        }

        #endregion
    }

    #endregion
}