#region Usings
// =================================================================================================
// This region contains all the necessary 'using' directives for this class.
// A 'using' directive imports a namespace, which allows you to use the types within it
// without having to fully qualify their names.
// =================================================================================================

// Using directives for the QuestPDF library, which is used to generate the final PDF report.
using FinalGameChecker.ViewModels;
using KellermanSoftware.CompareNetObjects;
// Using directive for the WPF-compatible OpenFileDialog.
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
// Using directives for the models and services from the shared common library.
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.Services;
// Standard .NET namespaces for core functionalities.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;

#endregion

namespace FinalGameChecker.Services
{
    #region ValidationResult Data Class
    /// <summary>
    /// A simple data-holding class to store the results of a single validation check.
    /// This makes it easier to manage and report on the outcome of each step in the checking process.
    /// Each instance represents one row in the validation summary table of the final report.
    /// This class is defined as public to be accessible within the namespace but is primarily used internally by the FinalChecks service.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Gets or sets the name of the check being performed (e.g., "LVW Pack Distribution"). This is displayed in the first column of the summary table.
        /// </summary>
        public string CheckName { get; set; }

        /// <summary>
        /// Gets or sets a string representing the expected outcome of the check.
        /// </summary>
        public string Expected { get; set; }

        /// <summary>
        /// Gets or sets a string representing the actual outcome found during the check.
        /// </summary>
        public string Actual { get; set; }

        /// <summary>
        /// Gets or sets the final status of the check, which can be "PASS", "FAIL", or "SKIPPED". This determines the styling in the report.
        /// </summary>
        public string Status { get; set; }
    }
    #endregion

    /// <summary>
    /// Performs final validation checks on all generated scratch card data files and generates a professional PDF report summarising the findings.
    /// This class is the core engine of the FinalGameChecker application, orchestrating all validation logic and report creation.
    /// It is designed to be modular, with separate methods for each distinct check and for the composition of each part of the PDF report.
    /// It now operates using a "checker" project file as the source of truth to validate against the original project and its output files.
    /// </summary>
    public class FinalChecks
    {
        #region Private Fields

        // A read-only field to hold the original project object from the generator. This is used only for the initial comparison.
        private readonly ScratchCardProject _originalProject;
        // A read-only field to hold the manually recreated checker project object. This is used as the "source of truth" for all subsequent validation.
        private readonly ScratchCardProject _checkerProject;
        // A read-only field to hold the list of all tickets generated for the Low-Value Winner (LVW) file.
        private readonly List<Ticket> _lvwTickets;
        // A read-only field to hold the list of all tickets generated for the High-Value Winner (HVW) file.
        private readonly List<Ticket> _hvwTickets;
        // A read-only field to hold the list of all tickets after they have been combined into the final print order. This represents the entire print run.
        private readonly List<Ticket> _combinedTickets;
        // A list to store the results of each individual validation check performed. This list is used to build the summary table in the final PDF report.
        private readonly List<ValidationResult> _validationResults = new List<ValidationResult>();
        // An instance of the shared GameWinValidator service to ensure consistent win logic validation.
        private readonly GameWinValidator _winValidator;
        // A list to store the detailed differences found between the two project files.
        private readonly List<DifferenceViewModel> _projectDifferences;

        // Dictionaries to store detailed tallies of wins for reporting purposes.
        private Dictionary<int, Dictionary<Tuple<decimal, bool>, int>> _sourceFileWinTally;
        private Dictionary<int, Dictionary<Tuple<decimal, bool>, int>> _combinedFileWinTally;
        private Dictionary<Tuple<decimal, bool>, int> _liveRunGrandTally;
        private Dictionary<Tuple<decimal, bool>, int> _printRunGrandTally;

        // Lists to hold security codes read from binary files.
        private List<int> _codes6Digit;
        private List<int> _codes3Digit;
        private List<int> _codes7Digit;

        // Fields to store file metadata for the report header.
        private readonly string _projectFileName;
        private readonly string _lvwFileName;
        private readonly string _hvwFileName;
        private readonly string _combinedFileName;
        private readonly DateTime _projectFileCreationDate;

        // Fields for the audit trail feature.
        private readonly string _checkedBy;
        private readonly DateTime _checkTimestamp;
        // A version number for the report, which can be incremented to indicate changes in the report format or content.
        private int _reportVersion = 1;

        #endregion

        #region Analysis Properties
        // These properties hold the calculated statistics for both the "Live Run" and "Print Run".
        // They are calculated once and then used to populate the analysis tables in the PDF report.

        private long LiveTotalTickets { get; set; }
        private long LiveLvwWinners { get; set; }
        private long LiveHvwWinners { get; set; }
        private long LiveTotalWinners { get; set; }
        private decimal LiveTotalSales { get; set; }
        private decimal LiveTotalPrizeFund { get; set; }
        private string LiveOdds { get; set; }
        private decimal LivePayoutPercentage { get; set; }

        private long PrintTotalTickets { get; set; }
        private long PrintLvwWinners { get; set; }
        private long PrintHvwWinners { get; set; }
        private long PrintTotalWinners { get; set; }
        private decimal PrintTotalSales { get; set; }
        private decimal PrintTotalPrizeFund { get; set; }
        private string PrintOdds { get; set; }
        private decimal PrintPayoutPercentage { get; set; }
        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the overall status of all checks combined. It will be "PASS" only if all individual checks pass or are skipped; otherwise, it will be "FAIL".
        /// This property is read by the main form to display the final result to the user.
        /// </summary>
        public string OverallStatus { get; private set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="FinalChecks"/> class, setting up the necessary data for the validation process.
        /// </summary>
        /// <param name="originalProject">The deserialised <see cref="ScratchCardProject"/> object from the original generator output.</param>
        /// <param name="checkerProject">The manually recreated <see cref="ScratchCardProject"/> object to be used as the source of truth.</param>
        /// <param name="lvwTickets">A list of <see cref="Ticket"/> objects from the LVW file.</param>
        /// <param name="hvwTickets">A list of <see cref="Ticket"/> objects from the HVW file.</param>
        /// <param name="combinedTickets">A list of <see cref="Ticket"/> objects from the final combined file.</param>
        /// <param name="projectFilePath">The full path to the original project file being checked.</param>
        /// <param name="lvwFilePath">The full path to the LVW file being checked.</param>
        /// <param name="hvwFilePath">The full path to the HVW file being checked.</param>
        /// <param name="combinedFilePath">The full path to the Combined file being checked.</param>
        /// <param name="projectDifferences">A list of differences found between the original and checker project files.</param>
        public FinalChecks(ScratchCardProject originalProject,
                           ScratchCardProject checkerProject,
                           List<Ticket> lvwTickets,
                           List<Ticket> hvwTickets,
                           List<Ticket> combinedTickets,
                           string projectFilePath,
                           string lvwFilePath,
                           string hvwFilePath,
                           string combinedFilePath,
                           List<DifferenceViewModel> projectDifferences)
        {
            // The constructor assigns the provided data to the class's private fields.
            // It uses null-coalescing operators to throw an ArgumentNullException if any of the required parameters are null, ensuring the class cannot be instantiated in an invalid state.
            _originalProject = originalProject ?? throw new ArgumentNullException(nameof(originalProject));
            _checkerProject = checkerProject ?? throw new ArgumentNullException(nameof(checkerProject));
            _lvwTickets = lvwTickets ?? throw new ArgumentNullException(nameof(lvwTickets));
            _hvwTickets = hvwTickets ?? throw new ArgumentNullException(nameof(hvwTickets));
            _combinedTickets = combinedTickets ?? throw new ArgumentNullException(nameof(combinedTickets));
            _projectDifferences = projectDifferences ?? throw new ArgumentNullException(nameof(projectDifferences));

            // Instantiate the shared validator service.
            _winValidator = new GameWinValidator();

            // Store the file names and creation date for the report header.
            _projectFileName = Path.GetFileName(projectFilePath);
            _lvwFileName = Path.GetFileName(lvwFilePath);
            _hvwFileName = Path.GetFileName(hvwFilePath);
            _combinedFileName = Path.GetFileName(combinedFilePath);
            _projectFileCreationDate = File.GetCreationTime(projectFilePath);

            // Capture audit trail information.
            _checkedBy = Environment.UserName;
            _checkTimestamp = DateTime.Now;

            // Sets the license for the QuestPDF library. The Community license is free and required to generate documents without a watermark.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        #endregion

        #region Main Execution Method

        /// <summary>
        /// Executes all validation checks in sequence and then generates a detailed PDF report of the findings.
        /// This is the main public method that orchestrates the entire checking process.
        /// </summary>
        /// <param name="outputDirectory">The directory where the final PDF report will be saved.</param>
        /// <returns>The full file path to the generated PDF report.</returns>
        public string ExecuteAndGenerateReport(string outputDirectory)
        {
            _validationResults.Clear();

            // The first check is always the comparison between the original and checker project files.
            var validationResult = new ValidationResult
            {
                CheckName = "Project File Verification",
                Expected = "Identical files",
                Actual = _projectDifferences.Any() ? "Mismatch Found" : "Identical",
                Status = _projectDifferences.Any() ? "FAIL" : "PASS"
            };
            _validationResults.Add(validationResult);

            // If the project files do not match, the check fails immediately.
            // No further checks are run, as they would be based on an invalid "source of truth".
            if (_projectDifferences.Any())
            {
                OverallStatus = "FAIL";
                string reportFilePath = GetNextAvailableReportPath(Path.Combine(outputDirectory, $"{_checkerProject.Settings.JobCode}-FinalCheckReport.pdf"));
                GeneratePdfReport(reportFilePath);
                return reportFilePath;
            }

            // If projects match, proceed with all other checks using the CHECKER's project file as the source of truth.
            CalculateRunTotals(_checkerProject);
            CheckLvwPacks(_checkerProject);
            CheckCombinedFile(_checkerProject);
            CheckHvwUniqueness(_checkerProject);
            ValidateAllGameWins(_checkerProject);
            // MODIFIED: This now routes to the correct check based on game type
            if (_checkerProject.Settings.IsPoundlandGame)
            {
                CheckPoundlandRedemptionFile(outputDirectory, _checkerProject);
            }
            else
            {
                CheckSecurityCodes(outputDirectory, _checkerProject);
            }
            CheckForSubPriceValues(_checkerProject);
            CheckForAccidentalWins(_checkerProject);
            CheckOnlinePrizeAssignment(_checkerProject);
            CheckForInternalGameErrors(_checkerProject);

            // Tally results for the detailed breakdown tables in the report.
            _sourceFileWinTally = TallyAllWins();
            _combinedFileWinTally = TallyCombinedWins();
            long liveTicketCount = (long)_checkerProject.Settings.TotalPacks * _checkerProject.Settings.CardsPerPack;
            _liveRunGrandTally = TallyGrandTotals(_combinedTickets.Take((int)liveTicketCount).ToList());
            _printRunGrandTally = TallyGrandTotals(_combinedTickets);

            // Determine the final overall status by checking if all validation results are either "PASS" or "SKIPPED".
            OverallStatus = _validationResults.All(r => r.Status == "PASS" || r.Status == "SKIPPED") ? "PASS" : "FAIL";

            // Construct the full file path for the PDF report.
            string baseReportPath = Path.Combine(outputDirectory, $"{_checkerProject.Settings.JobCode}-FinalCheckReport.pdf");
            string finalReportFilePath = GetNextAvailableReportPath(baseReportPath);

            // Call the method to generate the PDF document.
            GeneratePdfReport(finalReportFilePath);

            // Return the path to the newly created report.
            return finalReportFilePath;
        }

        #endregion

        #region Validation Steps

        /// <summary>
        /// Calculates the summary statistics for both the "Live Run" and "Print Run" based on the provided project settings.
        /// This logic replicates the analysis panel from the generator application and populates the properties used in the report.
        /// </summary>
        /// <param name="project">The project file to use as the source of truth for settings (this should be the checker's project).</param>
        private void CalculateRunTotals(ScratchCardProject project)
        {
            LiveTotalTickets = (long)project.Settings.TotalPacks * project.Settings.CardsPerPack;
            LiveLvwWinners = (long)project.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount) * project.Settings.TotalPacks;
            LiveHvwWinners = project.PrizeTiers.Sum(p => p.HvwWinnerCount);
            LiveTotalWinners = LiveLvwWinners + LiveHvwWinners;
            decimal liveLvwFund = (decimal)project.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount * (decimal)p.Value) * project.Settings.TotalPacks;
            decimal liveHvwFund = project.PrizeTiers.Sum(p => p.HvwWinnerCount * (decimal)p.Value);
            LiveTotalPrizeFund = liveLvwFund + liveHvwFund;
            LiveTotalSales = LiveTotalTickets * project.Settings.TicketSalePrice;
            LiveOdds = LiveTotalWinners > 0 ? $"1 in {(double)LiveTotalTickets / LiveTotalWinners:F2}" : "N/A";
            LivePayoutPercentage = LiveTotalSales > 0 ? (LiveTotalPrizeFund / LiveTotalSales) * 100 : 0;

            PrintTotalTickets = (long)project.Settings.PrintPacks * project.Settings.CardsPerPack;
            PrintLvwWinners = (long)project.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount) * project.Settings.PrintPacks;
            PrintHvwWinners = LiveHvwWinners;
            PrintTotalWinners = PrintLvwWinners + PrintHvwWinners;
            decimal printLvwFund = (decimal)project.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount * p.Value) * project.Settings.PrintPacks;
            PrintTotalPrizeFund = printLvwFund + liveHvwFund;
            PrintTotalSales = PrintTotalTickets * project.Settings.TicketSalePrice;
            PrintOdds = PrintTotalWinners > 0 ? $"1 in {(double)PrintTotalTickets / PrintTotalWinners:F2}" : "N/A";
            PrintPayoutPercentage = PrintTotalSales > 0 ? (PrintTotalPrizeFund / PrintTotalSales) * 100 : 0;
        }

        /// <summary>
        /// Checks if the distribution of low-value prizes is correct in every common pack of the LVW file, based on the checker's project settings.
        /// </summary>
        /// <param name="project">The checker's project file, used as the source of truth.</param>
        private void CheckLvwPacks(ScratchCardProject project)
        {
            int cardsPerPack = project.Settings.CardsPerPack;
            int totalCommonPacks = project.Settings.NoComPack;

            if (!_lvwTickets.Any() || totalCommonPacks == 0)
            {
                _validationResults.Add(new ValidationResult { CheckName = "LVW Pack Distribution", Status = "SKIPPED", Expected = "N/A", Actual = "No common packs to check." });
                return;
            }

            foreach (var prizeTier in project.PrizeTiers.Where(p => p.LvwWinnerCount > 0))
            {
                bool isOk = true;
                for (int packIndex = 0; packIndex < totalCommonPacks; packIndex++)
                {
                    var currentPackTickets = _lvwTickets.Skip(packIndex * cardsPerPack).Take(cardsPerPack).ToList();
                    int foundCount = currentPackTickets.Count(t => t.WinPrize.Value == prizeTier.Value && t.WinPrize.IsOnlinePrize == prizeTier.IsOnlinePrize);
                    if (foundCount != prizeTier.LvwWinnerCount)
                    {
                        isOk = false;
                        break;
                    }
                }

                string prizeIdentifier = prizeTier.IsOnlinePrize ? $"{prizeTier.DisplayText} (Online)" : prizeTier.DisplayText;
                _validationResults.Add(new ValidationResult
                {
                    CheckName = $"LVW Pack Distribution: {prizeIdentifier}",
                    Expected = prizeTier.LvwWinnerCount.ToString(),
                    Actual = isOk ? prizeTier.LvwWinnerCount.ToString() : "Mismatch Found",
                    Status = isOk ? "PASS" : "FAIL"
                });
            }
        }

        /// <summary>
        /// Performs several checks on the final combined file, using the checker's project as the source of truth for expected values.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void CheckCombinedFile(ScratchCardProject project)
        {
            long expectedTotalTickets = (long)project.Settings.PrintPacks * project.Settings.CardsPerPack;
            _validationResults.Add(new ValidationResult { CheckName = "Combined File: Total Ticket Count", Expected = expectedTotalTickets.ToString("N0"), Actual = _combinedTickets.Count.ToString("N0"), Status = _combinedTickets.Count == expectedTotalTickets ? "PASS" : "FAIL" });

            var hvwPrizeTiers = project.PrizeTiers.Where(p => p.HvwWinnerCount > 0).ToList();
            long hvwInCombinedFile = _combinedTickets.Count(t => hvwPrizeTiers.Any(p => p.Value == t.WinPrize.Value && p.IsOnlinePrize == t.WinPrize.IsOnlinePrize));
            _validationResults.Add(new ValidationResult { CheckName = "Combined File: High-Value Winner Count", Expected = _hvwTickets.Count.ToString("N0"), Actual = hvwInCombinedFile.ToString("N0"), Status = hvwInCombinedFile == _hvwTickets.Count ? "PASS" : "FAIL" });

            foreach (var hvwTier in hvwPrizeTiers)
            {
                int expectedCount = hvwTier.HvwWinnerCount;
                int foundCount = _combinedTickets.Count(t => t.WinPrize.Value == hvwTier.Value && t.WinPrize.IsOnlinePrize == hvwTier.IsOnlinePrize);
                string prizeIdentifier = hvwTier.IsOnlinePrize ? $"{hvwTier.DisplayText} (Online)" : hvwTier.DisplayText;
                _validationResults.Add(new ValidationResult { CheckName = $"HVW Distribution: {prizeIdentifier}", Expected = expectedCount.ToString(), Actual = foundCount.ToString(), Status = expectedCount == foundCount ? "PASS" : "FAIL" });
            }

            int cardsPerPack = project.Settings.CardsPerPack;
            var packsWithTooManyHvw = new List<int>();
            for (int packIndex = 0; packIndex < project.Settings.PrintPacks; packIndex++)
            {
                int hvwInPack = _combinedTickets.Skip(packIndex * cardsPerPack).Take(cardsPerPack).Count(t => hvwPrizeTiers.Any(p => p.Value == t.WinPrize.Value && p.IsOnlinePrize == t.WinPrize.IsOnlinePrize));
                if (hvwInPack > 2) packsWithTooManyHvw.Add(packIndex + 1);
            }
            _validationResults.Add(new ValidationResult { CheckName = "Combined File: Max 2 HVW per Pack", Expected = "0 failing packs", Actual = $"{packsWithTooManyHvw.Count} failing packs", Status = packsWithTooManyHvw.Any() ? "FAIL" : "PASS" });
        }

        /// <summary>
        /// Checks if all high-value winner tickets in the combined file are unique by generating a "fingerprint" for each one.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void CheckHvwUniqueness(ScratchCardProject project)
        {
            var hvwPrizeTiers = project.PrizeTiers.Where(p => p.HvwWinnerCount > 0).ToList();
            var hvwTicketsInCombined = _combinedTickets.Where(t => hvwPrizeTiers.Any(p => p.Value == t.WinPrize.Value && p.IsOnlinePrize == t.WinPrize.IsOnlinePrize)).ToList();
            var uniqueFingerprints = new HashSet<string>();
            int duplicateCount = 0;

            foreach (var ticket in hvwTicketsInCombined)
            {
                if (!uniqueFingerprints.Add(GenerateTicketFingerprint(ticket))) duplicateCount++;
            }
            _validationResults.Add(new ValidationResult { CheckName = "Combined File: HVW Ticket Uniqueness", Expected = "0 duplicates", Actual = $"{duplicateCount} duplicates", Status = duplicateCount > 0 ? "FAIL" : "PASS" });
        }

        /// <summary>
        /// Iterates through every winning ticket, delegating the validation of its game logic to the shared GameWinValidator service.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void ValidateAllGameWins(ScratchCardProject project)
        {
            var validationErrors = new List<string>();
            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                // Use the shared validator service to perform the check.
                _winValidator.ValidateTicketWin(_combinedTickets[i], project, i + 1, ref validationErrors);
            }
            _validationResults.Add(new ValidationResult { CheckName = "Game Logic: Win Validation", Expected = "0 errors", Actual = $"{validationErrors.Count} errors", Status = validationErrors.Any() ? "FAIL" : "PASS" });
        }
        /// <summary>
        /// Routes to the correct security/redemption file check based on the game type.
        /// </summary>
        /// <summary>
        /// Routes to the correct security/redemption file check based on the game type.
        /// </summary>
        private void CheckSecurityCodes(string projectDirectory, ScratchCardProject project)
        {
            // MODIFIED: This condition now correctly checks all possible security file configurations.
            // It will now only skip if BOTH the 3/7-digit combo AND the 6-digit file are missing.
            bool hasThreeAndSevenDigitFiles = !string.IsNullOrWhiteSpace(project.Security.ThreeDigitCodeFilePath) && !string.IsNullOrWhiteSpace(project.Security.SevenDigitCodeFilePath);
            bool hasSixDigitFile = !string.IsNullOrWhiteSpace(project.Security.SixDigitCodeFilePath);

            if (!hasThreeAndSevenDigitFiles && !hasSixDigitFile)
            {
                _validationResults.Add(new ValidationResult { CheckName = "Security: All Checks", Status = "SKIPPED", Actual = "No security files specified" });
                return;
            }

            LoadSecurityFiles(project);
            CheckSecurityFileUniqueness();

            string checkerContent = GenerateCheckerRedemptionContent(project);
            string originalContent = string.Empty;

            string gmcDir = Path.Combine(projectDirectory, "GMC");
            string originalFilePath = Path.Combine(gmcDir, $"{project.Settings.JobCode}-Redemption Codes.csv");

            if (!File.Exists(originalFilePath))
            {
                var result = MessageBox.Show($"Could not automatically find the redemption file at:\n{originalFilePath}\n\nWould you like to browse for it?", "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var ofd = new OpenFileDialog { Filter = "Redemption Codes File (*-Redemption Codes.csv)|*-Redemption Codes.csv", Title = "Select Original Redemption Codes File" };
                    if (ofd.ShowDialog() == true) originalFilePath = ofd.FileName;
                    else originalFilePath = null;
                }
                else
                {
                    originalFilePath = null;
                }
            }

            if (!string.IsNullOrEmpty(originalFilePath) && File.Exists(originalFilePath))
            {
                originalContent = File.ReadAllText(originalFilePath);
                bool isMatch = string.Equals(originalContent.Trim(), checkerContent.Trim(), StringComparison.Ordinal);
                _validationResults.Add(new ValidationResult { CheckName = "Security: Redemption Code Correlation", Expected = "Exact Match", Actual = isMatch ? "Exact Match" : "Mismatch Found", Status = isMatch ? "PASS" : "FAIL" });
                if (!isMatch)
                {
                    string checkerFilePath = Path.Combine(projectDirectory, $"{project.Settings.JobCode}-CHECKER-Redemption.csv");
                    File.WriteAllText(checkerFilePath, checkerContent, Encoding.UTF8);
                    MessageBox.Show($"A mismatch was found. The checker-generated redemption file has been saved for comparison:\n{checkerFilePath}", "Redemption Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                _validationResults.Add(new ValidationResult { CheckName = "Security: Redemption Code Correlation", Expected = "File to exist or be selected", Actual = "File not loaded", Status = "FAIL" });
            }
        }

        /// <summary>
        /// Performs checks specific to the Poundland redemption file.
        /// </summary>
        private void CheckPoundlandRedemptionFile(string projectDirectory, ScratchCardProject project)
        {
            if (string.IsNullOrWhiteSpace(project.Security.ThreeDigitCodeFilePath) || string.IsNullOrWhiteSpace(project.Security.SevenDigitCodeFilePath))
            {
                _validationResults.Add(new ValidationResult { CheckName = "Security: All Checks", Status = "SKIPPED", Actual = "3 and 7-digit security files were not specified." });
                return;
            }

            LoadSecurityFiles(project);
            CheckSecurityFileUniqueness();

            string checkerContent = GenerateCheckerPoundlandRedemptionContent(project);
            string originalContent = string.Empty;
            string gmcDir = Path.Combine(projectDirectory, "GMC");
            string originalFilePath = Path.Combine(gmcDir, $"{project.Settings.JobCode}Redemption Codes.txt");

            if (File.Exists(originalFilePath))
            {
                originalContent = File.ReadAllText(originalFilePath);
                bool isMatch = string.Equals(originalContent.Trim(), checkerContent.Trim(), StringComparison.Ordinal);
                _validationResults.Add(new ValidationResult { CheckName = "Security: Poundland Redemption Correlation", Expected = "Exact Match", Actual = isMatch ? "Exact Match" : "Mismatch Found", Status = isMatch ? "PASS" : "FAIL" });
            }
            else
            {
                _validationResults.Add(new ValidationResult { CheckName = "Security: Poundland Redemption Correlation", Expected = "File to exist", Actual = $"File not found: {Path.GetFileName(originalFilePath)}", Status = "FAIL" });
            }
        }

        #endregion

        #region New Validation Steps (Added)

        /// <summary>
        /// Checks all 'Match Prizes' games to ensure no displayed prize value is less than the ticket sale price.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void CheckForSubPriceValues(ScratchCardProject project)
        {
            var subPriceTickets = new List<int>();
            var prizeGames = project.Layout.GameModules.Where(m => m is MatchPrizesInGridGame || m is MatchPrizesInRowGame).ToList();

            if (!prizeGames.Any())
            {
                _validationResults.Add(new ValidationResult { CheckName = "Game Logic: Sub-Price Values", Status = "SKIPPED", Actual = "No prize-based games found." });
                return;
            }

            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                var ticket = _combinedTickets[i];
                foreach (var game in prizeGames)
                {
                    var playData = ticket.GameData.FirstOrDefault(pd => pd.GameNumber == game.GameNumber);
                    if (playData == null) continue;

                    foreach (int prizeIndex in playData.GeneratedSymbolIds)
                    {
                        if (prizeIndex >= 0 && prizeIndex < project.PrizeTiers.Count)
                        {
                            if (project.PrizeTiers[prizeIndex].Value < project.Settings.TicketSalePrice && project.PrizeTiers[prizeIndex].Value > 0)
                            {
                                subPriceTickets.Add(i + 1);
                                break; // Found one, no need to check other prizes on this ticket.
                            }
                        }
                    }
                    if (subPriceTickets.Contains(i + 1)) break; // Move to the next ticket.
                }
            }

            _validationResults.Add(new ValidationResult
            {
                CheckName = "Game Logic: Sub-Price Values",
                Expected = "0 tickets",
                Actual = $"{subPriceTickets.Count} tickets found",
                Status = subPriceTickets.Any() ? "FAIL" : "PASS"
            });
        }

        /// <summary>
        /// Checks every ticket to ensure it only wins for its assigned prize and does not contain accidental wins.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void CheckForAccidentalWins(ScratchCardProject project)
        {
            var accidentalWinTickets = new List<int>();
            var multiWinTickets = new List<int>();

            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                var ticket = _combinedTickets[i];
                int intendedWinCount = 0;
                int actualWinCount = 0;

                // Create a temporary ticket that is a loser, to test for accidental wins.
                var tempTicket = new Ticket { WinPrize = project.PrizeTiers.First(p => p.Value == 0), GameData = ticket.GameData };
                var errors = new List<string>();

                // Check each game module on the ticket.
                foreach (var gameModule in project.Layout.GameModules)
                {
                    // Check if this game was the INTENDED winner.
                    var gameData = ticket.GameData.FirstOrDefault(gd => gd.GameNumber == gameModule.GameNumber);
                    if (gameData != null && gameData.PrizeTierIndex >= 0)
                    {
                        intendedWinCount++;
                    }

                    // Now, check if this game's data constitutes a win, regardless of what was intended.
                    if (gameData != null && _winValidator.ValidateTicketWin(new Ticket { WinPrize = new PrizeTier { Value = 1 }, GameData = new List<GamePlayData> { gameData } }, project, i + 1, ref errors))
                    {
                        actualWinCount++;
                    }
                }

                // A ticket fails if it has more than one winning game panel.
                if (actualWinCount > 1)
                {
                    multiWinTickets.Add(i + 1);
                }
                // A ticket fails if the number of actual wins does not match the number of intended wins.
                // This catches losing tickets with a winning panel, or winning tickets where the wrong panel won.
                else if (actualWinCount != intendedWinCount)
                {
                    accidentalWinTickets.Add(i + 1);
                }
            }

            _validationResults.Add(new ValidationResult
            {
                CheckName = "Game Logic: Accidental Wins",
                Expected = "0 tickets",
                Actual = $"{accidentalWinTickets.Count} tickets found",
                Status = accidentalWinTickets.Any() ? "FAIL" : "PASS"
            });

            _validationResults.Add(new ValidationResult
            {
                CheckName = "Game Logic: Multiple Wins on One Ticket",
                Expected = "0 tickets",
                Actual = $"{multiWinTickets.Count} tickets found",
                Status = multiWinTickets.Any() ? "FAIL" : "PASS"
            });
        }

        /// <summary>
        /// Checks that any prize marked as 'IsOnlinePrize' is only ever won on an OnlineBonusGame module.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void CheckOnlinePrizeAssignment(ScratchCardProject project)
        {
            var misassignedOnlinePrizes = new List<int>();

            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                var ticket = _combinedTickets[i];
                // Find tickets that won an online prize.
                if (ticket.WinPrize.IsOnlinePrize && ticket.WinPrize.Value > 0)
                {
                    var winningGameData = ticket.GameData.FirstOrDefault(gd => gd.PrizeTierIndex >= 0);
                    if (winningGameData != null)
                    {
                        var winningModule = project.Layout.GameModules.FirstOrDefault(m => m.GameNumber == winningGameData.GameNumber);
                        // Check if the winning module is NOT an OnlineBonusGame.
                        if (winningModule != null && !(winningModule is OnlineBonusGame))
                        {
                            misassignedOnlinePrizes.Add(i + 1);
                        }
                    }
                }
            }

            _validationResults.Add(new ValidationResult
            {
                CheckName = "Game Logic: Online Prize Assignment",
                Expected = "0 tickets",
                Actual = $"{misassignedOnlinePrizes.Count} tickets found",
                Status = misassignedOnlinePrizes.Any() ? "FAIL" : "PASS"
            });
        }

        /// <summary>
        /// Checks every game panel on every ticket for internal integrity, specifically for multiple winning combinations within a single game.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void CheckForInternalGameErrors(ScratchCardProject project)
        {
            var multiWinGameTickets = new List<int>();

            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                var ticket = _combinedTickets[i];
                bool ticketHasError = false;

                foreach (var gameModule in project.Layout.GameModules)
                {
                    var gameData = ticket.GameData.FirstOrDefault(gd => gd.GameNumber == gameModule.GameNumber);
                    if (gameData == null) continue;

                    int winCountInModule = 0;

                    // Use a switch statement to apply counting logic based on the game type.
                    switch (gameModule)
                    {
                        case MatchSymbolsInGridGame msg:
                            winCountInModule = gameData.GeneratedSymbolIds.GroupBy(s => s).Count(g => g.Count() >= msg.SymbolsToMatch);
                            break;
                        case MatchPrizesInGridGame mpg:
                            winCountInModule = gameData.GeneratedSymbolIds.GroupBy(p => p).Count(g => g.Count() >= mpg.PrizesToMatch);
                            break;
                        case MatchSymbolsInRowGame msr:
                            for (int r = 0; r < msr.NumberOfRows; r++)
                            {
                                if (gameData.GeneratedSymbolIds.Skip(r * msr.SymbolsPerRow).Take(msr.SymbolsPerRow).GroupBy(s => s).Any(g => g.Count() >= msr.SymbolsToMatchInRow))
                                {
                                    winCountInModule++;
                                }
                            }
                            break;
                        case MatchPrizesInRowGame mpr:
                            for (int r = 0; r < mpr.NumberOfRows; r++)
                            {
                                if (gameData.GeneratedSymbolIds.Skip(r * mpr.PrizesPerRow).Take(mpr.PrizesPerRow).GroupBy(p => p).Any(g => g.Count() >= mpr.PrizesToMatchInRow))
                                {
                                    winCountInModule++;
                                }
                            }
                            break;
                    }

                    if (winCountInModule > 1)
                    {
                        multiWinGameTickets.Add(i + 1);
                        ticketHasError = true;
                        break; // Found an error on this ticket, no need to check its other games.
                    }
                }
            }

            _validationResults.Add(new ValidationResult
            {
                CheckName = "Game Logic: Internal Game Integrity",
                Expected = "0 tickets with multi-win games",
                Actual = $"{multiWinGameTickets.Count} tickets found",
                Status = multiWinGameTickets.Any() ? "FAIL" : "PASS"
            });
        }


        #endregion

        #region Helper and Tallying Methods

        /// <summary>
        /// Tallies all winning tickets from the source LVW and HVW files, grouping them by the game they were won on and by prize tier.
        /// </summary>
        /// <returns>A dictionary where the key is the game number and the value is another dictionary of prize counts for that game.</returns>
        private Dictionary<int, Dictionary<Tuple<decimal, bool>, int>> TallyAllWins()
        {
            var prizeTotalsByGame = new Dictionary<int, Dictionary<Tuple<decimal, bool>, int>>();
            var allSourceTickets = _lvwTickets.Concat(_hvwTickets);

            foreach (var ticket in allSourceTickets)
            {
                if (ticket.WinPrize.Value == 0) continue;

                var prizeKey = Tuple.Create((decimal)ticket.WinPrize.Value, ticket.WinPrize.IsOnlinePrize);
                var winningGame = ticket.GameData.FirstOrDefault(gd => gd.PrizeTierIndex >= 0);
                if (winningGame == null) continue;

                int gameNum = winningGame.GameNumber;

                if (!prizeTotalsByGame.ContainsKey(gameNum))
                    prizeTotalsByGame[gameNum] = new Dictionary<Tuple<decimal, bool>, int>();

                if (!prizeTotalsByGame[gameNum].ContainsKey(prizeKey))
                    prizeTotalsByGame[gameNum][prizeKey] = 0;

                prizeTotalsByGame[gameNum][prizeKey]++;
            }
            return prizeTotalsByGame;
        }

        /// <summary>
        /// Tallies all tickets (winners and losers) from the final combined file, grouping them by the game they were won on and by prize tier.
        /// </summary>
        /// <returns>A dictionary where the key is the game number (with 0 representing losers) and the value is another dictionary of prize counts.</returns>
        private Dictionary<int, Dictionary<Tuple<decimal, bool>, int>> TallyCombinedWins()
        {
            var prizeTotalsByGame = new Dictionary<int, Dictionary<Tuple<decimal, bool>, int>>();
            foreach (var ticket in _combinedTickets)
            {
                var prizeKey = Tuple.Create((decimal)ticket.WinPrize.Value, ticket.WinPrize.IsOnlinePrize);
                int gameNum;

                if (ticket.WinPrize.Value == 0)
                {
                    gameNum = 0;
                }
                else
                {
                    var winningGame = ticket.GameData.FirstOrDefault(gd => gd.PrizeTierIndex >= 0);
                    if (winningGame == null) continue;
                    gameNum = winningGame.GameNumber;
                }

                if (!prizeTotalsByGame.ContainsKey(gameNum))
                    prizeTotalsByGame[gameNum] = new Dictionary<Tuple<decimal, bool>, int>();

                if (!prizeTotalsByGame[gameNum].ContainsKey(prizeKey))
                    prizeTotalsByGame[gameNum][prizeKey] = 0;

                prizeTotalsByGame[gameNum][prizeKey]++;
            }
            return prizeTotalsByGame;
        }

        /// <summary>
        /// Tallies the grand total of each prize tier across a given list of tickets.
        /// </summary>
        /// <param name="ticketsToTally">The list of tickets to be tallied.</param>
        /// <returns>A dictionary where the key is the prize tier and the value is its total count.</returns>
        private Dictionary<Tuple<decimal, bool>, int> TallyGrandTotals(List<Ticket> ticketsToTally)
        {
            var grandTotals = new Dictionary<Tuple<decimal, bool>, int>();
            foreach (var ticket in ticketsToTally)
            {
                var prizeKey = Tuple.Create((decimal)ticket.WinPrize.Value, ticket.WinPrize.IsOnlinePrize);
                if (!grandTotals.ContainsKey(prizeKey))
                {
                    grandTotals[prizeKey] = 0;
                }
                grandTotals[prizeKey]++;
            }
            return grandTotals;
        }

        /// <summary>
        /// Generates a unique string "fingerprint" for a ticket based on its game data. This is used for uniqueness checks.
        /// </summary>
        /// <param name="ticket">The ticket to fingerprint.</param>
        /// <returns>A string representing the unique layout of the ticket's game data.</returns>
        private string GenerateTicketFingerprint(Ticket ticket)
        {
            var sb = new StringBuilder();
            foreach (var gameDataEntry in ticket.GameData.OrderBy(gd => gd.GameNumber))
            {
                sb.Append($"G{gameDataEntry.GameNumber}:");
                sb.Append(string.Join(",", gameDataEntry.GeneratedSymbolIds));
                sb.Append(";");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Loads all specified binary security code files into their respective lists in memory.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        private void LoadSecurityFiles(ScratchCardProject project)
        {
            _codes6Digit = ReadSecurityCodeFile(project.Security.SixDigitCodeFilePath);
            _codes3Digit = ReadSecurityCodeFile(project.Security.ThreeDigitCodeFilePath);
            _codes7Digit = ReadSecurityCodeFile(project.Security.SevenDigitCodeFilePath);
        }

        /// <summary>
        /// Reads a binary file containing a sequence of 32-bit integers and returns them as a list.
        /// </summary>
        /// <param name="filePath">The path to the binary file.</param>
        /// <returns>A list of integers from the file, or an empty list if an error occurs or the file does not exist.</returns>
        private List<int> ReadSecurityCodeFile(string filePath)
        {
            var codes = new List<int>();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return codes;
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    while (br.BaseStream.Position < br.BaseStream.Length) codes.Add(br.ReadInt32());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to read binary security file: {Path.GetFileName(filePath)}\n\nError: {ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return new List<int>();
            }
            return codes;
        }

        /// <summary>
        /// Checks if the codes within each loaded security file are unique. A security file with duplicate codes is invalid.
        /// </summary>
        private void CheckSecurityFileUniqueness()
        {
            if (_codes6Digit?.Any() == true) _validationResults.Add(new ValidationResult { CheckName = "Security: 6-Digit File Uniqueness", Expected = _codes6Digit.Count.ToString("N0"), Actual = new HashSet<int>(_codes6Digit).Count.ToString("N0"), Status = _codes6Digit.Count == new HashSet<int>(_codes6Digit).Count ? "PASS" : "FAIL" });
            if (_codes3Digit?.Any() == true) _validationResults.Add(new ValidationResult { CheckName = "Security: 3-Digit File Uniqueness", Expected = _codes3Digit.Count.ToString("N0"), Actual = new HashSet<int>(_codes3Digit).Count.ToString("N0"), Status = _codes3Digit.Count == new HashSet<int>(_codes3Digit).Count ? "PASS" : "FAIL" });
            if (_codes7Digit?.Any() == true) _validationResults.Add(new ValidationResult { CheckName = "Security: 7-Digit File Uniqueness", Expected = _codes7Digit.Count.ToString("N0"), Actual = new HashSet<int>(_codes7Digit).Count.ToString("N0"), Status = _codes7Digit.Count == new HashSet<int>(_codes7Digit).Count ? "PASS" : "FAIL" });
        }

        /// <summary>
        /// Gets the appropriate security code for a ticket based on its print order and the available security code files.
        /// </summary>
        /// <param name="printOrder">The 1-based index of the ticket in the final print run.</param>
        /// <param name="project">The current scratch card project.</param>
        /// <returns>The generated security code string, or an empty string if no valid codes are available.</returns>
        private string GetSecurityCode(int printOrder, ScratchCardProject project)
        {
            if (_codes6Digit?.Any() == true)
            {
                string prefix = project.Settings.JobCode.Length >= 3 ? project.Settings.JobCode.Substring(project.Settings.JobCode.Length - 3) : "???";
                string sec6 = _codes6Digit[(printOrder - 1) % _codes6Digit.Count].ToString("D6");
                return $"{prefix}{sec6}";
            }

            if (_codes3Digit?.Any() == true && _codes7Digit?.Any() == true)
            {
                string sec3 = _codes3Digit[(printOrder - 1) % _codes3Digit.Count].ToString("D3");
                string sec7 = _codes7Digit[(printOrder - 1) % _codes7Digit.Count].ToString("D7");
                return $"{project.Settings.JobNo}{sec3}{printOrder:D6}{sec7}";
            }
            return string.Empty;
        }

        /// <summary>
        /// Re-generates the entire content of the redemption CSV file from scratch for comparison purposes.
        /// </summary>
        /// <param name="project">The checker's project file.</param>
        /// <returns>A string containing the full CSV content, including the header row.</returns>
        private string GenerateCheckerRedemptionContent(ScratchCardProject project)
        {
            var sb = new StringBuilder();
            sb.AppendLine("QRCode,Barcode,WinType,PrizeAmount,EndDate,URL");
            var onlineBonusGame = project.Layout.GameModules.OfType<OnlineBonusGame>().FirstOrDefault();
            string baseUrl = onlineBonusGame?.Url ?? "";
            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                var ticket = _combinedTickets[i];
                int printOrder = i + 1;
                string securityCode = GetSecurityCode(printOrder, project);
                string fullUrl = !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(securityCode) ? $"{baseUrl}?code={securityCode}" : "";
                string winType = (ticket.WinPrize.Value == 0) ? "0" : (ticket.WinPrize.IsOnlinePrize ? "2" : "1");
                var prizeTier = project.PrizeTiers.FirstOrDefault(p => p.Value == ticket.WinPrize.Value && p.IsOnlinePrize == ticket.WinPrize.IsOnlinePrize);
                string barcode = prizeTier?.Barcode ?? "";
                sb.AppendLine($"\"{securityCode}\",\"{barcode}\",\"{winType}\",{ticket.WinPrize.Value:F2},{project.Settings.EndDate:dd/MM/yyyy},\"{fullUrl}\"");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates the content of a Poundland redemption TXT file for comparison.
        /// </summary>
        private string GenerateCheckerPoundlandRedemptionContent(ScratchCardProject project)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BarCode Number,Prize Amount");
            for (int i = 0; i < _combinedTickets.Count; i++)
            {
                var ticket = _combinedTickets[i];
                (int sec3, int sec7) = GetSecurityCodeParts(i + 1, project);
                string poundlandBarcode = GetPoundlandBarcode(sec3, sec7, project);
                sb.AppendLine($"{poundlandBarcode},{ticket.WinPrize.Value:F2}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Replicates the Poundland barcode generation formula from the main generator.
        /// </summary>
        private string GetPoundlandBarcode(int sec3, int sec7, ScratchCardProject project)
        {
            string prefix = project.Settings.PoundlandBarcodePrefix;
            string jobCodePart = project.Settings.JobCode.Length >= 3 ? project.Settings.JobCode.Substring(project.Settings.JobCode.Length - 3) : "000";
            return $"{prefix}{jobCodePart}{sec3}{sec7}";
        }

        /// <summary>
        /// Retrieves the raw 3-digit and 7-digit security codes for a specific ticket.
        /// </summary>
        private (int sec3, int sec7) GetSecurityCodeParts(int printOrder, ScratchCardProject project)
        {
            int sec3 = 0;
            int sec7 = 0;
            if (_codes3Digit?.Any() == true && _codes7Digit?.Any() == true)
            {
                sec3 = _codes3Digit[(printOrder - 1) % _codes3Digit.Count];
                sec7 = _codes7Digit[(printOrder - 1) % _codes7Digit.Count];
            }
            return (sec3, sec7);
        }

        #endregion

        #region PDF Report Generation

        /// <summary>
        /// Determines the next available versioned file path for the report.
        /// </summary>
        /// <param name="baseReportPath">The base path for the report (without a version number).</param>
        /// <returns>A unique, versioned file path.</returns>
        private string GetNextAvailableReportPath(string baseReportPath)
        {
            string directory = Path.GetDirectoryName(baseReportPath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(baseReportPath);
            string extension = Path.GetExtension(baseReportPath);

            _reportVersion = 1;
            string versionedPath = Path.Combine(directory, $"{fileNameWithoutExt}_v{_reportVersion}{extension}");

            while (File.Exists(versionedPath))
            {
                _reportVersion++;
                versionedPath = Path.Combine(directory, $"{fileNameWithoutExt}_v{_reportVersion}{extension}");
            }

            return versionedPath;
        }

        /// <summary>
        /// Generates the final multi-page PDF report using the QuestPDF library.
        /// </summary>
        /// <param name="filePath">The full path where the PDF file will be saved.</param>
        private void GeneratePdfReport(string filePath)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Header().Element(ComposeHeader);
                    page.Content().Element(ComposeContent);
                    page.Footer().AlignCenter().Text(x => { x.Span($"Version {_reportVersion} | Page "); x.CurrentPageNumber(); });
                });

                // Only add subsequent pages if the initial project check passed.
                if (OverallStatus != "FAIL" || _validationResults.Count > 1)
                {
                    container.Page(page =>
                    {
                        page.Margin(30);
                        page.Header().Element(ComposeHeader);
                        page.Content().Element(ComposeRunTotalsContent);
                        page.Footer().AlignCenter().Text(x => { x.Span($"Version {_reportVersion} | Page "); x.CurrentPageNumber(); }); ;
                    });

                    container.Page(page =>
                    {
                        page.Margin(30);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(9));
                        page.Size(PageSizes.A4.Landscape());

                        page.Header().Element(ComposeHeader);
                        page.Content().Element(ComposeTallyContent);
                        page.Footer().AlignCenter().Text(x => { x.Span($"Version {_reportVersion} | Page "); x.CurrentPageNumber(); });
                    });
                }
            }).GeneratePdf(filePath);
        }

        /// <summary>
        /// Composes the header section that appears at the top of each page of the PDF report.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeHeader(IContainer container)
        {
            var titleStyle = TextStyle.Default.FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
            var subtitleStyle = TextStyle.Default.FontSize(12).SemiBold();
            var fileInfoStyle = TextStyle.Default.FontSize(8).Italic();

            container.Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text($"Final Check Report: {_checkerProject.Settings.JobName}").Style(titleStyle);
                    column.Item().Text($"Job Code: {_checkerProject.Settings.JobCode} | Job No: {_checkerProject.Settings.JobNo}").Style(subtitleStyle);
                    column.Item().Text($"Checks Ran on: {_checkTimestamp:dddd, dd MMMM yyyy HH:mm}").Style(subtitleStyle);

                    column.Item().PaddingTop(5).Text($"Original Files Generated on: {_projectFileCreationDate:dddd, dd MMMM yyyy HH:mm}").Style(fileInfoStyle);
                    column.Item().Text(txt =>
                    {
                        txt.Span("Files Checked: ").Style(fileInfoStyle);
                        txt.Span($"{_projectFileName}, {_lvwFileName}, {_hvwFileName}, {_combinedFileName}").Style(fileInfoStyle);
                    });

                    // Add the audit trail information to the header.
                    column.Item().PaddingTop(5).Text(txt =>
                    {
                        txt.Span("Report generated by: ").FontSize(8).SemiBold();
                        txt.Span($"{_checkedBy}").FontSize(8);
                    });
                });

                row.ConstantItem(100).Column(col =>
                {
                    col.Item().AlignCenter().Element(ComposeLogo);
                    col.Item().PaddingTop(5);
                    col.Item().Border(2).BorderColor(OverallStatus == "PASS" ? Colors.Green.Medium : Colors.Red.Medium)
                        .Background(OverallStatus == "PASS" ? Colors.Green.Lighten4 : Colors.Red.Lighten4)
                        .Padding(5).AlignCenter().Text(OverallStatus).FontSize(16).Bold();
                });
            });
        }

        /// <summary>
        /// Composes the company logo element for the header.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeLogo(IContainer container)
        {
            var logoPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "Resource", "hslogo.png");
            if (File.Exists(logoPath))
            {
                byte[] imageData = File.ReadAllBytes(logoPath);
                container.MaxWidth(80).MaxHeight(50).Image(imageData);
            }
        }

        /// <summary>
        /// Composes the main content for the first page, which consists of the Validation Summary table.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeContent(IContainer container)
        {
            container.PaddingVertical(20).Column(column =>
            {
                column.Spacing(20);
                column.Item().Element(ComposeValidationSummaryTable);
                if (_projectDifferences.Any())
                {
                    column.Item().Element(ComposeDifferencesTable);
                }
            });
        }

        /// <summary>
        /// Composes the content for the second page, which consists of the Live vs. Print Run Totals table.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeRunTotalsContent(IContainer container)
        {
            container.PaddingVertical(20).Column(column =>
            {
                column.Spacing(20);
                column.Item().Text("Run Totals Analysis", TextStyle.Default.FontSize(16).SemiBold());
                column.Item().Element(ComposeRunTotalsTable);
            });
        }

        /// <summary>
        /// Composes the content for the third, landscape page, which contains all the detailed win tally tables.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeTallyContent(IContainer container)
        {
            container.PaddingVertical(20).Column(column =>
            {
                column.Spacing(20);
                column.Item().Text("Source Files Win Tally (LVW + HVW)", TextStyle.Default.FontSize(14).SemiBold());
                column.Item().Element(c => ComposeWinSummaryTable(c, _sourceFileWinTally));
                column.Item().Text("Combined File Win Tally (by Game)", TextStyle.Default.FontSize(14).SemiBold());
                column.Item().Element(c => ComposeWinSummaryTable(c, _combinedFileWinTally));
                column.Item().Text("Live Run Grand Total", TextStyle.Default.FontSize(14).SemiBold());
                column.Item().Element(c => ComposeGrandTotalTable(c, _liveRunGrandTally));
                column.Item().Text("", TextStyle.Default.FontSize(14).SemiBold()); // Blank for Formatting
                column.Item().Text("Print Run Grand Total", TextStyle.Default.FontSize(14).SemiBold());
                column.Item().Element(c => ComposeGrandTotalTable(c, _printRunGrandTally));
            });
        }

        /// <summary>
        /// Composes the table that shows the summary of all validation checks performed (PASS/FAIL/SKIPPED).
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeValidationSummaryTable(IContainer container)
        {
            var sortedResults = _validationResults
                .OrderBy(result =>
                {
                    if (result.CheckName.StartsWith("HVW Distribution:")) return 0;
                    if (result.CheckName.StartsWith("LVW Pack Distribution:")) return 1;
                    return 2;
                })
                .ThenByDescending(result =>
                {
                    if (result.CheckName.StartsWith("HVW Distribution:") || result.CheckName.StartsWith("LVW Pack Distribution:"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(result.CheckName, @"£?([\d,]+(\.\d{2})?)");
                        if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(",", ""), out var value))
                        {
                            return value;
                        }
                    }
                    return decimal.MinValue;
                })
                .ThenByDescending(result => result.CheckName.Contains("(Online)"))
                .ThenBy(result => result.CheckName)
                .ToList();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(60);
                });
                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten2).Text("Validation Check").Bold();
                    header.Cell().Background(Colors.Grey.Lighten2).Text("Expected Value").Bold();
                    header.Cell().Background(Colors.Grey.Lighten2).Text("Actual Value").Bold();
                    header.Cell().Background(Colors.Grey.Lighten2).AlignCenter().Text("Status").Bold();
                    header.Cell().ColumnSpan(4).BorderBottom(1).BorderColor(Colors.Black);
                });
                foreach (var result in sortedResults)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(result.CheckName);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(result.Expected);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(result.Actual);
                    var statusCell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2);
                    switch (result.Status)
                    {
                        case "PASS":
                            statusCell.Background(Colors.Green.Lighten4).AlignCenter().Text(text =>
                            {
                                text.Span(result.Status).FontColor(Colors.Green.Darken2).Bold();
                            });
                            break;
                        case "FAIL":
                            statusCell.Background(Colors.Red.Lighten4).AlignCenter().Text(text =>
                            {
                                text.Span(result.Status).FontColor(Colors.Red.Darken2).Bold();
                            });
                            break;
                        case "SKIPPED":
                            statusCell.Background(Colors.Amber.Lighten4).AlignCenter().Text(text =>
                            {
                                text.Span(result.Status).FontColor(Colors.Amber.Darken2).Bold();
                            });
                            break;
                        default:
                            statusCell.AlignCenter().Text(text =>
                            {
                                text.Span(result.Status).Bold();
                            });
                            break;
                    }
                }
            });
        }

        /// <summary>
        /// Composes the table that shows the Live vs. Print run totals, providing a high-level statistical overview.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        private void ComposeRunTotalsTable(IContainer container)
        {
            var culture = new CultureInfo("en-GB");
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten2).Text("Metric").Bold();
                    header.Cell().Background(Colors.Grey.Lighten2).AlignCenter().Text("Live Run").Bold();
                    header.Cell().Background(Colors.Grey.Lighten2).AlignCenter().Text("Print Run").Bold();
                    header.Cell().ColumnSpan(3).BorderBottom(1).BorderColor(Colors.Black);
                });

                Action<string, string, string> addRow = (metric, liveValue, printValue) =>
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(metric).SemiBold();
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignCenter().Text(liveValue);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignCenter().Text(printValue);
                };

                addRow("Total Tickets", LiveTotalTickets.ToString("N0"), PrintTotalTickets.ToString("N0"));
                addRow("LVW Winners", LiveLvwWinners.ToString("N0"), PrintLvwWinners.ToString("N0"));
                addRow("HVW Winners", LiveHvwWinners.ToString("N0"), PrintHvwWinners.ToString("N0"));
                addRow("Total Winners", LiveTotalWinners.ToString("N0"), PrintTotalWinners.ToString("N0"));
                addRow("Total Sales", LiveTotalSales.ToString("C", culture), PrintTotalSales.ToString("C", culture));
                addRow("Total Prize Fund", LiveTotalPrizeFund.ToString("C", culture), PrintTotalPrizeFund.ToString("C", culture));
                addRow("Overall Odds", LiveOdds, PrintOdds);
                addRow("Payout Percentage", $"{LivePayoutPercentage:F2}%", $"{PrintPayoutPercentage:F2}%");
            });
        }

        /// <summary>
        /// Composes a table that shows a detailed breakdown of wins per game.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        /// <param name="prizeTotalsByGame">The dictionary containing the tallied prize data to display.</param>
        private void ComposeWinSummaryTable(IContainer container, Dictionary<int, Dictionary<Tuple<decimal, bool>, int>> prizeTotalsByGame)
        {
            var winnablePrizeTiers = _checkerProject.PrizeTiers
                .Where(p => p.Value > 0)
                .OrderBy(p => p.Value)
                .ThenBy(p => p.IsOnlinePrize)
                .ToList();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(120);
                    foreach (var _ in winnablePrizeTiers) columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten2).Text("Game").Bold();
                    foreach (var tier in winnablePrizeTiers) header.Cell().Background(Colors.Grey.Lighten2).AlignCenter().Text($"{tier.DisplayText}{(tier.IsOnlinePrize ? " (O)" : "")}").Bold();
                });

                foreach (var gameEntry in prizeTotalsByGame.OrderBy(g => g.Key))
                {
                    if (gameEntry.Key == 0) continue;
                    var module = _checkerProject.Layout.GameModules.FirstOrDefault(m => m.GameNumber == gameEntry.Key);
                    string gameName = $"Game {gameEntry.Key}";
                    if (module is OnlineBonusGame) gameName += " (Online QRCode)";

                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(gameName);
                    foreach (var tier in winnablePrizeTiers)
                    {
                        var key = Tuple.Create((decimal)tier.Value, tier.IsOnlinePrize);
                        string count = gameEntry.Value.TryGetValue(key, out int value) ? value.ToString("N0") : "0";
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignCenter().Text(count);
                    }
                }
            });
        }

        /// <summary>
        /// Composes a grand total table, showing the total count of each prize tier across all games for a given dataset.
        /// </summary>
        /// <param name="container">The container provided by QuestPDF to place content into.</param>
        /// <param name="grandTotals">The dictionary containing the pre-tallied grand total data to display.</param>
        private void ComposeGrandTotalTable(IContainer container, Dictionary<Tuple<decimal, bool>, int> grandTotals)
        {
            var allDefinedTiers = _checkerProject.PrizeTiers
                .OrderBy(p => p.Value)
                .ThenBy(p => p.IsOnlinePrize)
                .ToList();

            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(120);
                    foreach (var _ in allDefinedTiers) columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Grey.Lighten2).Text("Category").Bold();
                    foreach (var tier in allDefinedTiers)
                    {
                        header.Cell().Background(Colors.Grey.Lighten2).AlignCenter().Text($"{tier.DisplayText}{(tier.IsOnlinePrize ? " (O)" : "")}").Bold();
                    }
                });

                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text("Grand Total");
                foreach (var tier in allDefinedTiers)
                {
                    var key = Tuple.Create((decimal)tier.Value, tier.IsOnlinePrize);
                    string count = grandTotals.TryGetValue(key, out int value) ? value.ToString("N0") : "0";
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignCenter().Text(count);
                }
            });
        }

        /// <summary>
        /// Composes a new table to display the specific differences found between the two project files.
        /// </summary>
        private void ComposeDifferencesTable(IContainer container)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2); // Area
                    columns.RelativeColumn(3); // Property
                    columns.RelativeColumn(2); // Original Value
                    columns.RelativeColumn(2); // Checker Value
                });

                table.Header(header =>
                {
                    // Main header for the differences section.
                    header.Cell().ColumnSpan(4).Background(Colors.Red.Lighten4).Padding(5)
                        .Text("Project File Differences")
                        .Bold().FontColor(Colors.Red.Darken2);

                    // Column headers.
                    header.Cell().Background(Colors.Grey.Lighten3).Text("Area").Bold();
                    header.Cell().Background(Colors.Grey.Lighten3).Text("Property").Bold();
                    header.Cell().Background(Colors.Grey.Lighten3).Text("Original Value").Bold();
                    header.Cell().Background(Colors.Grey.Lighten3).Text("Checker Value").Bold();
                });

                // Iterate through each difference found by the comparison library.
                foreach (var diff in _projectDifferences)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(diff.Area);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(diff.Property);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(diff.OriginalValue);
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(diff.CheckerValue);
                }
            });
        }
        #endregion
    }
}
