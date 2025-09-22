#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using Microsoft.Win32;
using ScratchCardGenerator.Common.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;

#endregion

namespace ScratchCardGenerator.Services
{
    /// <summary>
    /// Provides services for analysing and verifying scratch card data files after they have been generated.
    /// This service reads the generated JSON data files and produces highly detailed CSV analysis reports,
    /// providing insight into prize distribution and game statistics, acting as a key quality assurance tool.
    /// </summary>
    public class FileAnalysisService
    {
        #region Private Fields

        /// <summary>
        /// A delegate action used to send status update messages back to the ViewModel.
        /// This allows the service to provide real-time, non-blocking feedback to the user via the application's status bar.
        /// </summary>
        private readonly Action<string> _updateStatusCallback;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="FileAnalysisService"/> class.
        /// </summary>
        /// <param name="updateStatusCallback">The callback action for reporting status updates to the UI.</param>
        public FileAnalysisService(Action<string> updateStatusCallback)
        {
            _updateStatusCallback = updateStatusCallback;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Analyses a Low-Value Winner (LVW) JSON file and generates a detailed CSV report.
        /// The report includes prize distribution verification per pack, a detailed list of all winning tickets,
        /// and a summary of total wins per game number.
        /// </summary>
        /// <param name="project">The current scratch card project, used for rule validation (e.g., CardsPerPack).</param>
        public void CheckLvwFile(ScratchCardProject project)
        {
            try
            {
                // Prompt the user to select the JSON file to analyse.
                var tickets = ReadTicketFile("Select LVW JSON File to Check (*-lvw.json)");
                if (tickets == null)
                {
                    _updateStatusCallback?.Invoke("LVW check cancelled by user.");
                    return;
                }

                _updateStatusCallback?.Invoke("Analysing LVW file...");

                var settings = project.Settings;
                var analysis = new StringBuilder();
                var winsByGame = new Dictionary<int, int>();

                // --- Report Header ---
                analysis.AppendLine("--- LVW FILE ANALYSIS REPORT ---");
                analysis.AppendLine($"File checked: {tickets.Count} tickets found.");
                analysis.AppendLine();

                // --- Section 1: Analyse prize distribution per pack ---
                analysis.AppendLine("--- Prize Distribution (per pack) Verification ---");
                for (int packIndex = 0; packIndex < settings.NoComPack; packIndex++)
                {
                    // Isolate the tickets belonging to the current pack.
                    var packTickets = tickets.Skip(packIndex * settings.CardsPerPack).Take(settings.CardsPerPack).ToList();
                    bool packIsValid = true;

                    // For each prize tier that should have LVW winners, verify the count in this specific pack.
                    foreach (var prizeTier in project.PrizeTiers.Where(p => p.LvwWinnerCount > 0))
                    {
                        // Use a robust check on both Value and IsOnlinePrize to correctly identify the prize tier,
                        // as there can be online and offline prizes of the same value.
                        int foundCount = packTickets.Count(t => t.WinPrize.Value == prizeTier.Value && t.WinPrize.IsOnlinePrize == prizeTier.IsOnlinePrize);
                        if (foundCount != prizeTier.LvwWinnerCount)
                        {
                            string prizeIdentifier = prizeTier.IsOnlinePrize ? $"{prizeTier.DisplayText} (Online)" : prizeTier.DisplayText;
                            analysis.AppendLine($"***ERROR*** Pack {packIndex + 1}: Prize tier '{prizeIdentifier}' count is wrong. Expected {prizeTier.LvwWinnerCount}, Found {foundCount}.");
                            packIsValid = false;
                        }
                    }

                    if (packIsValid)
                    {
                        analysis.AppendLine($"Pack {packIndex + 1}: Prize distribution is correct.");
                    }
                }
                analysis.AppendLine();

                // --- Section 2: Create a detailed list of every winning ticket ---
                analysis.AppendLine("--- Detailed Win List (LVW) ---");
                analysis.AppendLine("Pack,Ticket In Pack,Win Amount,Online,Winning Game Number");
                for (int i = 0; i < tickets.Count; i++)
                {
                    var ticket = tickets[i];
                    // Process only the winning tickets.
                    if (ticket.WinPrize.Value > 0)
                    {
                        int packNum = (i / settings.CardsPerPack) + 1;
                        int ticketNum = (i % settings.CardsPerPack) + 1;

                        // To find the winning game, we look for the GamePlayData entry that has a valid PrizeTierIndex.
                        var winningEntry = ticket.GameData.FirstOrDefault(gd => gd.PrizeTierIndex >= 0);

                        // If no winning entry is found on a winning ticket, this indicates a generation error. Skip it.
                        if (winningEntry == null) continue;

                        // Safely get the game number from the found entry.
                        int winningGameNum = winningEntry.GameNumber;

                        analysis.AppendLine($"{packNum},{ticketNum},\"{ticket.WinPrize.DisplayText}\",{ticket.WinPrize.IsOnlinePrize},{winningGameNum}");

                        // Tally the win for the per-game breakdown summary.
                        if (winsByGame.ContainsKey(winningGameNum))
                            winsByGame[winningGameNum]++;
                        else
                            winsByGame.Add(winningGameNum, 1);
                    }
                }
                analysis.AppendLine();

                // --- Section 3: Create a summary breakdown of wins per game ---
                analysis.AppendLine("--- Wins Per Game Breakdown ---");
                analysis.AppendLine("Game Number,Total Wins");
                foreach (var gameWin in winsByGame.OrderBy(g => g.Key))
                {
                    analysis.AppendLine($"Game {gameWin.Key},{gameWin.Value}");
                }

                // --- Save the final report ---
                SaveReport(analysis.ToString(), "Save LVW Analysis Report", $"{settings.JobCode}-lvw-analysis.csv");
            }
            catch (Exception ex)
            {
                _updateStatusCallback?.Invoke("Error during LVW file analysis.");
                MessageBox.Show($"An error occurred during file check: {ex.Message}", "Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Analyses a High-Value Winner (HVW) JSON file and generates a detailed CSV report.
        /// The report includes verification of the total prize counts, a list of all winning tickets,
        /// and a breakdown of total wins per game.
        /// </summary>
        /// <param name="project">The current scratch card project for rule validation.</param>
        public void CheckHvwFile(ScratchCardProject project)
        {
            try
            {
                var tickets = ReadTicketFile("Select HVW JSON File to Check (*-hvw.json)");
                if (tickets == null)
                {
                    _updateStatusCallback?.Invoke("HVW check cancelled by user.");
                    return;
                }

                _updateStatusCallback?.Invoke("Analysing HVW file...");

                var analysis = new StringBuilder();
                var winsByGame = new Dictionary<int, int>();

                // --- Report Header ---
                analysis.AppendLine("--- HVW FILE ANALYSIS REPORT ---");
                analysis.AppendLine($"Total Winning Tickets Found: {tickets.Count}");
                analysis.AppendLine();

                // --- Section 1: Analyse total prize distribution across the whole file ---
                analysis.AppendLine("--- Prize Distribution Verification ---");
                analysis.AppendLine("Prize,Online,Expected,Found,Status");
                bool allCountsMatch = true;
                foreach (var prizeTier in project.PrizeTiers.Where(p => p.HvwWinnerCount > 0))
                {
                    int expectedCount = prizeTier.HvwWinnerCount;
                    // Use a robust check on both Value and IsOnlinePrize to correctly identify the prize tier.
                    int foundCount = tickets.Count(t => t.WinPrize.Value == prizeTier.Value && t.WinPrize.IsOnlinePrize == prizeTier.IsOnlinePrize);
                    string status = (expectedCount == foundCount) ? "OK" : "FAIL";
                    if (status == "FAIL") allCountsMatch = false;
                    analysis.AppendLine($"\"{prizeTier.DisplayText}\",{prizeTier.IsOnlinePrize},{expectedCount},{foundCount},{status}");
                }
                analysis.AppendLine();
                analysis.AppendLine(allCountsMatch ? "Overall Status: PASS" : "Overall Status: FAIL - Mismatched counts found!");
                analysis.AppendLine();

                // --- Section 2: Create a detailed list of all winning tickets ---
                analysis.AppendLine("--- Detailed Win List (HVW) ---");
                analysis.AppendLine("Ticket Number,Win Amount,Online,Winning Game Number");
                for (int i = 0; i < tickets.Count; i++)
                {
                    var ticket = tickets[i];

                    var winningEntry = ticket.GameData.FirstOrDefault(gd => gd.PrizeTierIndex >= 0);
                    if (winningEntry == null) continue; // Skip malformed tickets.
                    int winningGameNum = winningEntry.GameNumber;

                    analysis.AppendLine($"{i + 1},\"{ticket.WinPrize.DisplayText}\",{ticket.WinPrize.IsOnlinePrize},{winningGameNum}");

                    // Tally the win for the per-game breakdown summary.
                    if (winsByGame.ContainsKey(winningGameNum))
                        winsByGame[winningGameNum]++;
                    else
                        winsByGame.Add(winningGameNum, 1);
                }
                analysis.AppendLine();

                // --- Section 3: Create a summary breakdown of wins per game ---
                analysis.AppendLine("--- Wins Per Game Breakdown ---");
                analysis.AppendLine("Game Number,Total Wins");
                foreach (var gameWin in winsByGame.OrderBy(g => g.Key))
                {
                    analysis.AppendLine($"Game {gameWin.Key},{gameWin.Value}");
                }

                // --- Save the final report ---
                SaveReport(analysis.ToString(), "Save HVW Analysis Report", $"{project.Settings.JobCode}-hvw-analysis.csv");
            }
            catch (Exception ex)
            {
                _updateStatusCallback?.Invoke("Error during HVW file analysis.");
                MessageBox.Show($"An error occurred during file check: {ex.Message}", "Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Prompts the user to select a JSON ticket file and deserialises it into a list of full Ticket objects.
        /// </summary>
        /// <param name="title">The title for the file open dialog.</param>
        /// <returns>A list of Ticket objects, or null if the user cancels or an error occurs.</returns>
        private List<Ticket> ReadTicketFile(string title)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Ticket File (*.json)|*.json|All Files (*.*)|*.*",
                Title = title
            };

            if (ofd.ShowDialog() != true) return null; // User cancelled the dialog.

            try
            {
                _updateStatusCallback?.Invoke($"Reading file: {Path.GetFileName(ofd.FileName)}...");
                string jsonString = File.ReadAllText(ofd.FileName);
                var tickets = JsonSerializer.Deserialize<List<Ticket>>(jsonString) ?? new List<Ticket>();
                _updateStatusCallback?.Invoke("File read successfully.");
                return tickets;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading or deserialising file '{Path.GetFileName(ofd.FileName)}'.\n{ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Prompts the user with a save dialog and writes the provided report content to the selected file.
        /// </summary>
        /// <param name="reportContent">The string content of the report to save.</param>
        /// <param name="title">The title for the save file dialog.</param>
        /// <param name="fileName">The default file name for the report.</param>
        private void SaveReport(string reportContent, string title, string fileName)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "CSV Analysis Report (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = title,
                FileName = fileName
            };

            if (sfd.ShowDialog() == true)
            {
                // Write the report content to the selected file, explicitly using UTF8 encoding.
                // This is important to ensure special characters (like the pound currency symbol £) are handled correctly in programs like Excel.
                File.WriteAllText(sfd.FileName, reportContent, Encoding.UTF8);
                _updateStatusCallback?.Invoke("Analysis complete. Report saved.");
                MessageBox.Show($"Analysis complete. Report saved to:\n{sfd.FileName}", "Check Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _updateStatusCallback?.Invoke("Analysis report not saved.");
            }
        }

        #endregion
    }
}