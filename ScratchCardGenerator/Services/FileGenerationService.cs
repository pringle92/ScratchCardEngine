#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using BarcodeStandard;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

#endregion

namespace ScratchCardGenerator.Services
{
    /// <summary>
    /// Provides the core services for generating all scratch card data files, reports, and PDFs.
    /// This class orchestrates the entire generation process, from creating initial ticket data to combining
    /// it into a final print run and producing all necessary production and verification reports.
    /// </summary>
    public class FileGenerationService
    {
        #region Private Fields

        /// <summary>
        /// A callback action used to send real-time status update messages back to the ViewModel for display in the UI.
        /// </summary>
        private readonly Action<string> _updateStatusCallback;

        /// <summary>
        /// An <see cref="IProgress{T}"/> implementation used to report percentage completion back to the UI's progress bar during long-running operations.
        /// </summary>
        private readonly IProgress<int> _progressReporter;

        /// <summary>
        /// A single, shared instance of the <see cref="Random"/> class to ensure a consistent and non-repeating sequence of random numbers throughout a generation process.
        /// </summary>
        private readonly Random _random = new Random();

        /// <summary>
        /// A service responsible for creating the final data integrity manifest file (checksums).
        /// </summary>
        private readonly ManifestCreationService _manifestService;

        /// <summary>
        /// A service responsible for validating the game logic of winning tickets to ensure their integrity.
        /// </summary>
        private readonly GameWinValidator _winValidator;

        // The following fields are used to manage file streams for reading large binary security code files.
        // This approach avoids loading potentially huge files entirely into memory.
        private BinaryReader _reader6Digit;
        private BinaryReader _reader3Digit;
        private BinaryReader _reader7Digit;
        private long _count6Digit;
        private long _count3Digit;
        private long _count7Digit;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="FileGenerationService"/> class.
        /// </summary>
        /// <param name="updateStatusCallback">The callback action for reporting status updates to the UI.</param>
        /// <param name="progress">The progress reporter to send percentage updates to the UI.</param>
        public FileGenerationService(Action<string> updateStatusCallback, IProgress<int> progress)
        {
            _updateStatusCallback = updateStatusCallback;
            _progressReporter = progress;
            _winValidator = new GameWinValidator();
            _manifestService = new ManifestCreationService();
        }

        #endregion

        #region Top-Level "One-Click" Generation Method

        /// <summary>
        /// Runs the complete end-to-end generation process. It generates LVW and HVW data in memory,
        /// combines them into a final print run, and writes all associated report files without
        /// requiring intermediate user interaction.
        /// </summary>
        /// <param name="project">The current project containing all settings and rules.</param>
        public async Task RunFullGenerationAsync(ScratchCardProject project)
        {
            await Task.Run(async () =>
            {
                _updateStatusCallback?.Invoke("Starting full generation process...");
                _progressReporter?.Report(0);

                // --- Step 1: Preparation ---
                string jobDirectory = PrepareAndGetJobDirectory(project);
                if (string.IsNullOrEmpty(jobDirectory))
                {
                    _updateStatusCallback?.Invoke("Generation aborted: Job directory could not be prepared.");
                    _progressReporter?.Report(0);
                    return;
                }

                // Perform pre-flight configuration checks.
                if (!PerformPreFlightChecks(project))
                {
                    _progressReporter?.Report(0);
                    return;
                }

                try
                {
                    // Load security files at the beginning of the process.
                    LoadSecurityFiles(project);

                    // --- Step 2: Generate LVW and HVW Ticket Data in memory---
                    _updateStatusCallback?.Invoke("Step 1 of 3: Generating LVW and HVW ticket data...");
                    var (lvwTickets, hvwTickets) = GenerateTicketData(project);
                    _progressReporter?.Report(25);

                    _updateStatusCallback?.Invoke("Step 2 of 3: Combining data and writing all report files...");
                    await FinalizeGenerationAndWriteReports(project, lvwTickets, hvwTickets, jobDirectory);

                    _updateStatusCallback?.Invoke("Step 3 of 3: Process complete.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Successfully completed full generation.\n\nAll reports have been saved to:\n{Path.Combine(jobDirectory, "GMC")}", "Full Generation Complete", MessageBoxButton.OK, MessageBoxImage.Information));
                }
                catch (Exception ex)
                {
                    _updateStatusCallback?.Invoke("A critical error occurred during full generation.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred during the generation process: {ex.Message}", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    CloseSecurityFiles();
                    _progressReporter?.Report(0);
                }
            });
        }

        #endregion

        #region Public File Generation Methods

        /// <summary>
        /// Asynchronously generates the Low-Value Winner (LVW) data file.
        /// This file contains the data for the "common packs" which are repeated to form the bulk of the print run.
        /// </summary>
        /// <param name="project">The current project containing all settings and rules.</param>
        public async Task GenerateLvwFile(ScratchCardProject project)
        {
            await Task.Run(() =>
            {
                _updateStatusCallback?.Invoke("Starting LVW file generation...");
                _progressReporter?.Report(0);

                string jobDirectory = PrepareAndGetJobDirectory(project);
                if (string.IsNullOrEmpty(jobDirectory))
                {
                    _updateStatusCallback?.Invoke("Generation aborted: Job directory could not be prepared.");
                    _progressReporter?.Report(0);
                    return;
                }

                bool hasActiveOnlinePrizes = project.PrizeTiers.Any(p => p.IsOnlinePrize && p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0));
                bool hasOnlineModule = project.Layout.GameModules.OfType<OnlineBonusGame>().Any();
                if (hasActiveOnlinePrizes && !hasOnlineModule)
                {
                    _updateStatusCallback?.Invoke("Generation failed: Online prizes exist with no Online Bonus module.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("This project contains active online prizes but does not have an 'Online Bonus (QR)' module on the card layout.\n\nPlease either set the online prize counts to zero or add the 'Online Bonus (QR)' module to the layout before generating files.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    _progressReporter?.Report(0);
                    return;
                }

                try
                {
                    var settings = project.Settings;
                    var validPrizes = project.PrizeTiers
                        .Where(p => !p.IsOnlineDrawOnly && (p.Value >= settings.TicketSalePrice || p.Value == 0))
                        .ToList();

                    var generatedTickets = new List<Ticket>();
                    var uniqueTicketFingerprints = new HashSet<string>();

                    _updateStatusCallback?.Invoke("Creating LVW prize distribution...");

                    var packDistribution = new List<PrizeTier>();
                    foreach (var prizeTier in validPrizes)
                    {
                        for (int i = 0; i < prizeTier.LvwWinnerCount; i++)
                        {
                            packDistribution.Add(prizeTier);
                        }
                    }

                    if (packDistribution.Count > settings.CardsPerPack)
                    {
                        _updateStatusCallback?.Invoke("LVW Generation Error.");
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error: LVW winner counts ({packDistribution.Count}) exceed cards per pack ({settings.CardsPerPack}).", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                        _progressReporter?.Report(0);
                        return;
                    }

                    var loserPrize = validPrizes.FirstOrDefault(p => p.Value == 0);
                    if (loserPrize == null)
                    {
                        _updateStatusCallback?.Invoke("LVW Generation Error.");
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Error: A prize tier with a value of 0 (for losers) must be defined.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                        _progressReporter?.Report(0);
                        return;
                    }

                    while (packDistribution.Count < settings.CardsPerPack)
                    {
                        packDistribution.Add(loserPrize);
                    }

                    _updateStatusCallback?.Invoke($"Generating {settings.NoComPack} common packs...");
                    for (int pack = 0; pack < settings.NoComPack; pack++)
                    {
                        if (settings.NoComPack > 0) _progressReporter?.Report((pack + 1) * 100 / settings.NoComPack);

                        var shuffledPack = packDistribution.OrderBy(x => _random.Next()).ToList();
                        for (int i = 0; i < settings.CardsPerPack; i++)
                        {
                            Ticket ticket;
                            string fingerprint;
                            int attempts = 0;

                            do
                            {
                                ticket = new Ticket { WinPrize = shuffledPack[i] };
                                GameModule winningModule = SelectWinningModule(project, ticket.WinPrize, _random);
                                foreach (var module in project.Layout.GameModules)
                                {
                                    bool isWinningModule = (module == winningModule);
                                    module.GeneratePlayData(ticket, isWinningModule, ticket.WinPrize, project, _random);
                                }
                                fingerprint = GenerateTicketFingerprint(ticket);
                                attempts++;
                            } while (!uniqueTicketFingerprints.Add(fingerprint) && attempts < 1000);

                            if (attempts >= 1000)
                            {
                                _updateStatusCallback?.Invoke("Could not generate a unique ticket. Aborting.");
                                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Failed to generate a unique ticket after 1000 attempts. The symbol/game variety might be too low.", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                                _progressReporter?.Report(0);
                                return;
                            }

                            var validationErrors = new List<string>();
                            if (!_winValidator.ValidateTicketWin(ticket, project, generatedTickets.Count + 1, ref validationErrors))
                            {
                                string errorMsg = $"A critical internal error occurred during LVW generation. A generated ticket failed self-validation.\n\nErrors:\n- {string.Join("\n- ", validationErrors)}\n\nGeneration will be aborted.";
                                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(errorMsg, "Generation Self-Test Failed", MessageBoxButton.OK, MessageBoxImage.Error));
                                _progressReporter?.Report(0);
                                return;
                            }
                            generatedTickets.Add(ticket);
                        }
                    }
                    _progressReporter?.Report(100);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var sfd = new SaveFileDialog
                        {
                            Filter = "LVW JSON File (*.json)|*.json",
                            Title = "Save LVW File",
                            FileName = $"{project.Settings.JobCode}-lvw.json",
                            InitialDirectory = Path.Combine(jobDirectory, "Game Files")
                        };

                        if (sfd.ShowDialog() == true)
                        {
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            string jsonString = JsonSerializer.Serialize(generatedTickets, options);
                            File.WriteAllText(sfd.FileName, jsonString);
                            _updateStatusCallback?.Invoke($"LVW file saved successfully.");
                            MessageBox.Show($"Successfully generated and saved {generatedTickets.Count} LVW tickets.", "LVW Generation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            _updateStatusCallback?.Invoke("LVW file generation cancelled.");
                        }
                    });
                }
                catch (InvalidOperationException ex)
                {
                    _updateStatusCallback?.Invoke("Generation Failed: Configuration Error.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show(ex.Message, "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                catch (Exception ex)
                {
                    _updateStatusCallback?.Invoke("An error occurred during LVW generation.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred during LVW generation: {ex.Message}", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    _progressReporter?.Report(0);
                }

            });
        }

        /// <summary>
        /// Asynchronously generates the High-Value Winner (HVW) data file.
        /// This file contains a discrete list of all high-value winning tickets for the entire job.
        /// </summary>
        /// <param name="project">The current project containing all settings and rules.</param>
        public async Task GenerateHvwFile(ScratchCardProject project)
        {
            await Task.Run(() =>
            {
                _updateStatusCallback?.Invoke("Starting HVW file generation...");
                _progressReporter?.Report(0);

                string jobDirectory = PrepareAndGetJobDirectory(project);
                if (string.IsNullOrEmpty(jobDirectory))
                {
                    _updateStatusCallback?.Invoke("Generation aborted: Job directory could not be prepared.");
                    _progressReporter?.Report(0);
                    return;
                }

                bool hasActiveOnlinePrizes = project.PrizeTiers.Any(p => p.IsOnlinePrize && p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0));
                bool hasOnlineModule = project.Layout.GameModules.OfType<OnlineBonusGame>().Any();
                if (hasActiveOnlinePrizes && !hasOnlineModule)
                {
                    _updateStatusCallback?.Invoke("Generation failed: Online prizes exist with no Online Bonus module.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("This project contains active online prizes but does not have an 'Online Bonus (QR)' module on the card layout.\n\nPlease either set the online prize counts to zero or add the 'Online Bonus (QR)' module to the layout before generating files.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    _progressReporter?.Report(0);
                    return;
                }

                try
                {
                    var settings = project.Settings;
                    var validPrizes = project.PrizeTiers
                        .Where(p => !p.IsOnlineDrawOnly && (p.Value >= settings.TicketSalePrice || p.Value == 0))
                        .ToList();

                    var generatedTickets = new List<Ticket>();
                    var uniqueTicketFingerprints = new HashSet<string>();

                    var hvwPrizes = validPrizes.Where(p => p.HvwWinnerCount > 0).ToList();
                    int totalHvwToGenerate = hvwPrizes.Sum(p => p.HvwWinnerCount);
                    if (totalHvwToGenerate == 0)
                    {
                        _updateStatusCallback?.Invoke("No HVW prizes to generate.");
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("There are no High-Value Winners configured in the prize tiers.", "No HVW Prizes", MessageBoxButton.OK, MessageBoxImage.Information));
                        _progressReporter?.Report(0);
                        return;
                    }
                    int hvwGeneratedCount = 0;

                    foreach (var prizeTier in hvwPrizes)
                    {
                        for (int i = 0; i < prizeTier.HvwWinnerCount; i++)
                        {
                            Ticket ticket;
                            string fingerprint;
                            int attempts = 0;

                            do
                            {
                                ticket = new Ticket { WinPrize = prizeTier };
                                GameModule winningModule = SelectWinningModule(project, ticket.WinPrize, _random);
                                foreach (var module in project.Layout.GameModules)
                                {
                                    bool isWinningModule = (module == winningModule);
                                    module.GeneratePlayData(ticket, isWinningModule, ticket.WinPrize, project, _random);
                                }
                                fingerprint = GenerateTicketFingerprint(ticket);
                                attempts++;
                            } while (!uniqueTicketFingerprints.Add(fingerprint) && attempts < 1000);

                            if (attempts >= 1000)
                            {
                                _updateStatusCallback?.Invoke("Could not generate a unique HVW ticket. Aborting.");
                                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Failed to generate a unique HVW ticket after 1000 attempts.", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                                _progressReporter?.Report(0);
                                return;
                            }

                            var validationErrors = new List<string>();
                            if (!_winValidator.ValidateTicketWin(ticket, project, generatedTickets.Count + 1, ref validationErrors))
                            {
                                string errorMsg = $"A critical internal error occurred during HVW generation. A generated ticket failed self-validation.\n\nErrors:\n- {string.Join("\n- ", validationErrors)}\n\nGeneration will be aborted.";
                                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(errorMsg, "Generation Self-Test Failed", MessageBoxButton.OK, MessageBoxImage.Error));
                                _progressReporter?.Report(0);
                                return;
                            }

                            generatedTickets.Add(ticket);
                            hvwGeneratedCount++;
                            _progressReporter?.Report(hvwGeneratedCount * 100 / totalHvwToGenerate);
                        }
                    }

                    _progressReporter?.Report(100);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var sfd = new SaveFileDialog
                        {
                            Filter = "HVW JSON File (*.json)|*.json",
                            Title = "Save HVW File",
                            FileName = $"{project.Settings.JobCode}-hvw.json",
                            InitialDirectory = Path.Combine(jobDirectory, "Game Files")
                        };

                        if (sfd.ShowDialog() == true)
                        {
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            string jsonString = JsonSerializer.Serialize(generatedTickets, options);
                            File.WriteAllText(sfd.FileName, jsonString);
                            _updateStatusCallback?.Invoke($"HVW file saved successfully.");
                            MessageBox.Show($"Successfully generated and saved {generatedTickets.Count} HVW tickets.", "HVW Generation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            _updateStatusCallback?.Invoke("HVW file generation cancelled.");
                        }
                    });
                }
                catch (InvalidOperationException ex)
                {
                    _updateStatusCallback?.Invoke("Generation Failed: Configuration Error.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show(ex.Message, "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                catch (Exception ex)
                {
                    _updateStatusCallback?.Invoke("An error occurred during HVW generation.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred during HVW generation: {ex.Message}", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    _progressReporter?.Report(0);
                }
            });
        }

        /// <summary>
        /// Asynchronously performs the final step of generation: combining the LVW and HVW files into a
        /// final print run, and creating all associated production and verification reports.
        /// </summary>
        /// <param name="project">The current project containing all settings and rules.</param>
        public async Task GenerateCombinedFile(ScratchCardProject project)
        {
            await Task.Run(() =>
            {
                _updateStatusCallback?.Invoke("Starting combined file generation...");
                _progressReporter?.Report(0);

                string jobDirectory = PrepareAndGetJobDirectory(project);
                if (string.IsNullOrEmpty(jobDirectory))
                {
                    _updateStatusCallback?.Invoke("Generation aborted: Job directory could not be prepared.");
                    _progressReporter?.Report(0);
                    return;
                }

                var gmcDir = Path.Combine(jobDirectory, "GMC");
                Directory.CreateDirectory(gmcDir);

                try
                {
                    // Open streams to the required security files.
                    LoadSecurityFiles(project);

                    List<Ticket> lvwTickets = null;
                    List<Ticket> hvwTickets = null;

                    // Prompt user to select the LVW and HVW JSON files.
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        lvwTickets = ReadTicketFile("Select LVW JSON File (*-lvw.json)", Path.Combine(jobDirectory, "Game Files"));
                        if (lvwTickets == null) { _updateStatusCallback?.Invoke("File combination cancelled."); return; }
                        hvwTickets = ReadTicketFile("Select HVW JSON File (*-hvw.json)", Path.Combine(jobDirectory, "Game Files"));
                        if (hvwTickets == null) { _updateStatusCallback?.Invoke("File combination cancelled."); return; }
                    });

                    if (lvwTickets == null || hvwTickets == null) { _progressReporter?.Report(0); return; }

                    // Combine LVW and HVW tickets into a single source list.
                    var allTickets = lvwTickets.Concat(hvwTickets).ToList();
                    var settings = project.Settings;
                    int totalPrintTickets = settings.TotalTickets;
                    if (totalPrintTickets <= 0)
                    {
                        Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Total print tickets is zero. Please check job settings.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning));
                        _progressReporter?.Report(0); return;
                    }

                    // --- Ticket Shuffling and HVW Placement Algorithm ---
                    _updateStatusCallback?.Invoke("Shuffling tickets into print positions...");
                    _progressReporter?.Report(10);

                    // artktPos maps a final print position (index) to a ticket index in the 'allTickets' source list.
                    var artktPos = new int[totalPrintTickets + 1];
                    // arLoser is a temporary array to mark which print positions are initially occupied by LVW winners.
                    var arLoser = new int[totalPrintTickets + 1];
                    // arPackPQty tracks how many HVW have been placed into each pack to enforce a limit (e.g., max 2 per pack).
                    var arPackPQty = new byte[settings.PrintPacks + 1];
                    // artktShuf is a temporary array for shuffling ticket positions within a single pack.
                    var artktShuf = Enumerable.Range(0, settings.CardsPerPack).Select(i => (byte)i).ToArray();

                    // This loop populates the print run by repeating the common packs.
                    int ticketReportCount = (settings.NoComPack > 0) ? (int)Math.Ceiling((double)settings.PrintPacks / settings.NoComPack) : 0;
                    for (int rpt = 1; rpt <= ticketReportCount; rpt++)
                    {
                        for (int pack = 1; pack <= settings.NoComPack; pack++)
                        {
                            Shuffle(artktShuf, _random);
                            for (int tinpack = 1; tinpack <= settings.CardsPerPack; tinpack++)
                            {
                                int tpos = ((rpt - 1) * settings.TotalCommonTickets) + ((pack - 1) * settings.CardsPerPack) + tinpack;
                                if (tpos > totalPrintTickets) continue;
                                int fposn = ((pack - 1) * settings.CardsPerPack) + artktShuf[tinpack - 1];
                                artktPos[tpos] = fposn; // Map the print position to a source LVW ticket index.
                                if (fposn < lvwTickets.Count && lvwTickets[fposn].WinPrize.Value > 0) arLoser[tpos] = 1; // Mark if this position holds an LVW winner.
                            }
                        }
                    }

                    _updateStatusCallback?.Invoke("Placing high-value winners...");
                    _progressReporter?.Report(30);

                    // Create a shuffled list of all available "loser" slots within the live run.
                    var availableLoserSlots = new List<int>();
                    int totalLiveTickets = settings.TotalPacks * settings.CardsPerPack;
                    for (int i = 1; i <= totalLiveTickets; i++) if (i < arLoser.Length && arLoser[i] == 0) availableLoserSlots.Add(i);
                    Shuffle(availableLoserSlots, _random);

                    // Place each HVW into an available loser slot, respecting the max-per-pack rule.
                    int hvwIndex = 0;
                    foreach (var hvwTicket in hvwTickets)
                    {
                        bool placed = false;
                        for (int i = 0; i < availableLoserSlots.Count; i++)
                        {
                            int tpos = availableLoserSlots[i];
                            int rpack = (tpos - 1) / settings.CardsPerPack + 1;
                            if (rpack < arPackPQty.Length && arPackPQty[rpack] < 2) // Check if pack can accept another HVW
                            {
                                artktPos[tpos] = lvwTickets.Count + hvwIndex; // Map this print position to an HVW ticket index.
                                arPackPQty[rpack]++; // Increment the HVW count for this pack.
                                availableLoserSlots.RemoveAt(i); // Remove the slot so it can't be used again.
                                placed = true;
                                break;
                            }
                        }
                        if (!placed)
                        {
                            Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Error: Could not find a suitable empty slot to place a high-value winner.", "Placement Error", MessageBoxButton.OK, MessageBoxImage.Error));
                            _progressReporter?.Report(0); return;
                        }
                        hvwIndex++;
                    }

                    // Create the final list of tickets in their correct print order.
                    var finalPrintRunTickets = new List<Ticket>();
                    for (int i = 1; i <= totalPrintTickets; i++) if (i < artktPos.Length && artktPos[i] < allTickets.Count) finalPrintRunTickets.Add(allTickets[artktPos[i]]);

                    _updateStatusCallback?.Invoke("Generating reports...");
                    _progressReporter?.Report(50);
                    Application.Current.Dispatcher.Invoke(async () =>
                    {
                        var sfd = new SaveFileDialog
                        {
                            Filter = "Combined JSON File (*.json)|*.json",
                            Title = "Save Combined JSON File (All CSV Reports Will Save to the Same Directory)",
                            FileName = $"{settings.JobNo}-{settings.JobCode}-combined.json",
                            InitialDirectory = gmcDir
                        };

                        if (sfd.ShowDialog() == true)
                        {
                            string outputDir = Path.GetDirectoryName(sfd.FileName);
                            string baseFileName = Path.GetFileNameWithoutExtension(sfd.FileName).Replace("-combined", "");

                            _updateStatusCallback?.Invoke("Writing combined JSON file...");
                            File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(finalPrintRunTickets, new JsonSerializerOptions { WriteIndented = true }));
                            _progressReporter?.Report(60);

                            _updateStatusCallback?.Invoke("Writing main CSV report...");
                            WriteMainCsvReport(Path.Combine(gmcDir, $"{baseFileName}.csv"), project, allTickets, artktPos);
                            _progressReporter?.Report(70);

                            _updateStatusCallback?.Invoke("Writing last pack CSV for Poundland game...");
                            WriteLastPackCsv(gmcDir, project, allTickets, artktPos);
                            _progressReporter?.Report(72);

                            _updateStatusCallback?.Invoke("Writing summary report...");
                            WriteSummaryCsvReport(Path.Combine(gmcDir, "Game_Summary.csv"), project);
                            _progressReporter?.Report(75);

                            _updateStatusCallback?.Invoke("Writing samples report...");
                            WriteSamplesCsv(Path.Combine(gmcDir, $"{settings.JobNo}-{settings.JobCode}-SAMPLES.csv"), project, lvwTickets);
                            _progressReporter?.Report(80);

                            // Suppress unnecessary files for Poundland games
                            if (!project.Settings.IsPoundlandGame)
                            {
                                _updateStatusCallback?.Invoke("Writing customer verification report...");
                                WriteCustVerCsv(Path.Combine(gmcDir, $"{settings.JobCode}-CUSTVER.csv"), project, allTickets);
                                _progressReporter?.Report(85);

                                _updateStatusCallback?.Invoke("Writing prize barcode report...");
                                WriteSimplePrizeBarcodeCsv(Path.Combine(gmcDir, $"{settings.JobCode}-PrizeBarcodes.csv"), project);
                                _progressReporter?.Report(90);

                                _updateStatusCallback?.Invoke("Writing combined barcodes text file...");
                                WriteCombinedBarcodesTxt(Path.Combine(gmcDir, $"{settings.JobCode}-PrizeBarcodes.txt"), project);
                                _progressReporter?.Report(95);
                            }

                            if (project.Settings.IsPoundlandGame)
                            {
                                _updateStatusCallback?.Invoke("Writing Poundland redemption report...");
                                WritePoundlandRedemptionFile(Path.Combine(gmcDir, $"{settings.JobCode}Redemption Codes.txt"), project, allTickets, artktPos);
                            }
                            else
                            {
                                _updateStatusCallback?.Invoke("Writing security redemption report...");
                                WriteSecurityRedemptionCsv(Path.Combine(gmcDir, $"{settings.JobCode}-Redemption Codes.csv"), project, allTickets, artktPos);
                            }
                            _progressReporter?.Report(100);

                            string combinedJsonPath = sfd.FileName;
                            string lvwTicketsPath = Path.Combine(jobDirectory, "Game Files", $"{settings.JobCode}-lvw.json");
                            string hvwTicketsPath = Path.Combine(jobDirectory, "Game Files", $"{settings.JobCode}-hvw.json");
                            string mainCsvPath = Path.Combine(gmcDir, $"{baseFileName}.csv");

                            string redemptionFilePath;
                            if (project.Settings.IsPoundlandGame)
                            {
                                redemptionFilePath = Path.Combine(gmcDir, $"{settings.JobCode}Redemption Codes.txt");
                            }
                            else
                            {
                                redemptionFilePath = Path.Combine(gmcDir, $"{settings.JobCode}-Redemption Codes.csv");
                            }

                            _updateStatusCallback?.Invoke("Creating data integrity manifest...");
                            var filesToHash = new List<string>
                                {
                                    lvwTicketsPath,
                                    hvwTicketsPath,
                                    combinedJsonPath,
                                    mainCsvPath,
                                    redemptionFilePath
                                };
                            string manifestPath = Path.Combine(outputDir, "data_manifest.sha256");
                            await _manifestService.CreateManifestFileAsync(filesToHash, manifestPath);
                            _updateStatusCallback?.Invoke("Data integrity manifest created.");

                            _updateStatusCallback?.Invoke("All reports saved successfully.");
                            MessageBox.Show($"Successfully combined files and saved all reports to:\n{gmcDir}", "Combination Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            _updateStatusCallback?.Invoke("File combination cancelled.");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _updateStatusCallback?.Invoke("An error occurred during file combination.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred during file combination: {ex.Message}", "Combination Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    CloseSecurityFiles();
                    _progressReporter?.Report(0);
                }
            });
        }
        #endregion

        #region Barcode PDF Generation

        /// <summary>
        /// Asynchronously generates a PDF document containing all barcodes for the project using the QuestPDF library.
        /// </summary>
        public async Task PrintBarcodesPdf(ScratchCardProject project)
        {
            await Task.Run(() =>
            {
                _updateStatusCallback?.Invoke("Generating barcodes PDF...");
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var settings = project.Settings;
                        var sfd = new SaveFileDialog
                        {
                            Filter = "PDF Document (*.pdf)|*.pdf",
                            Title = "Save Barcodes PDF",
                            FileName = $"{settings.JobNo} - {settings.JobCode} - Prize Bar Codes.pdf"
                        };

                        if (sfd.ShowDialog() == true)
                        {
                            QuestPDF.Settings.License = LicenseType.Community;
                            Document.Create(container =>
                            {
                                container.Page(page =>
                                {
                                    page.Size(PageSizes.A4);
                                    page.Margin(2, Unit.Centimetre);
                                    page.PageColor(Colors.White);
                                    page.DefaultTextStyle(x => x.FontSize(12));

                                    page.Header().AlignCenter().Text($"{settings.JobNo} - {settings.JobName} - {settings.JobCode}").SemiBold().FontSize(16);

                                    page.Content().Column(column =>
                                    {
                                        column.Spacing(20);
                                        column.Item().AlignCenter().Text($"{settings.Client}").FontSize(14);
                                        column.Item().AlignCenter().Text("Barcode Type EAN 13").Italic();

                                        AddBarcodeRow(column.Item(), new PrizeTier { Barcode = settings.ProductBarcode, DisplayText = "Product Barcode" }, null);
                                        AddBarcodeRow(column.Item(), new PrizeTier { Barcode = settings.LoserBarcode, DisplayText = "NO WIN" }, null);

                                        var winnablePrizes = project.PrizeTiers
                                            .Where(p => p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0))
                                            .GroupBy(p => p.Barcode)
                                            .Select(g => g.First())
                                            .OrderByDescending(p => p.Value);

                                        foreach (var prize in winnablePrizes)
                                        {
                                            AddBarcodeRow(column.Item(), prize, prize.IsOnlinePrize);
                                        }
                                    });

                                    page.Footer().AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
                                });
                            }).GeneratePdf(sfd.FileName);

                            _updateStatusCallback?.Invoke("Barcodes PDF generated successfully.");
                            MessageBox.Show($"Successfully generated Barcodes PDF to:\n{sfd.FileName}", "PDF Generation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            _updateStatusCallback?.Invoke("Barcode PDF generation cancelled.");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _updateStatusCallback?.Invoke("An error occurred during PDF generation.");
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred during PDF generation: {ex.Message}", "PDF Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        #endregion

        #region Private Core Logic (Refactored)

        /// <summary>
        /// Performs pre-flight checks common to all generation workflows to catch critical errors early.
        /// </summary>
        /// <returns>True if all checks pass, otherwise false.</returns>
        private bool PerformPreFlightChecks(ScratchCardProject project)
        {
            // Check 1: If online prizes exist, an online module must exist.
            bool hasActiveOnlinePrizes = project.PrizeTiers.Any(p => p.IsOnlinePrize && p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0));
            bool hasOnlineModule = project.Layout.GameModules.OfType<OnlineBonusGame>().Any();
            if (hasActiveOnlinePrizes && !hasOnlineModule)
            {
                _updateStatusCallback?.Invoke("Generation failed: Online prizes exist with no Online Bonus module.");
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("This project contains active online prizes but does not have an 'Online Bonus (QR)' module on the card layout.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                return false;
            }

            // Check 2: The total number of low-value winners must not exceed the pack size.
            var settings = project.Settings;
            var validPrizes = project.PrizeTiers.Where(p => !p.IsOnlineDrawOnly).ToList();
            int lvwCountInPack = validPrizes.Sum(p => p.LvwWinnerCount);

            if (lvwCountInPack > settings.CardsPerPack)
            {
                _updateStatusCallback?.Invoke("LVW Generation Error.");
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error: Total LVW winner counts ({lvwCountInPack}) exceed cards per pack ({settings.CardsPerPack}).", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                return false;
            }

            // Check 3: A "loser" prize tier (value = 0) must exist.
            if (!validPrizes.Any(p => p.Value == 0))
            {
                _updateStatusCallback?.Invoke("LVW Generation Error.");
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Error: A prize tier with a value of 0 (for losers) must be defined.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                return false;
            }

            // All checks passed.
            return true;
        }

        /// <summary>
        /// Contains the core, non-UI logic for generating LVW and HVW ticket data in memory.
        /// </summary>
        /// <returns>A tuple containing the list of LVW tickets and the list of HVW tickets.</returns>
        private (List<Ticket> lvwTickets, List<Ticket> hvwTickets) GenerateTicketData(ScratchCardProject project)
        {
            var settings = project.Settings;
            var validPrizes = project.PrizeTiers
                .Where(p => !p.IsOnlineDrawOnly && (p.Value >= settings.TicketSalePrice || p.Value == 0))
                .ToList();

            var lvwTickets = new List<Ticket>();
            var hvwTickets = new List<Ticket>();
            var uniqueLvwFingerprints = new HashSet<string>();
            var uniqueHvwFingerprints = new HashSet<string>();

            // --- Generate LVW Data ---
            var packDistribution = new List<PrizeTier>();
            foreach (var prizeTier in validPrizes)
            {
                for (int i = 0; i < prizeTier.LvwWinnerCount; i++) packDistribution.Add(prizeTier);
            }
            var loserPrize = validPrizes.First(p => p.Value == 0);
            while (packDistribution.Count < settings.CardsPerPack) packDistribution.Add(loserPrize);

            for (int pack = 0; pack < settings.NoComPack; pack++)
            {
                var shuffledPack = packDistribution.OrderBy(x => _random.Next()).ToList();
                for (int i = 0; i < settings.CardsPerPack; i++)
                {
                    Ticket ticket = GenerateSingleTicket(shuffledPack[i], project, uniqueLvwFingerprints, lvwTickets.Count + 1);
                    lvwTickets.Add(ticket);
                }
            }

            // --- Generate HVW Data ---
            var hvwPrizes = validPrizes.Where(p => p.HvwWinnerCount > 0).ToList();
            foreach (var prizeTier in hvwPrizes)
            {
                for (int i = 0; i < prizeTier.HvwWinnerCount; i++)
                {
                    Ticket ticket = GenerateSingleTicket(prizeTier, project, uniqueHvwFingerprints, hvwTickets.Count + 1);
                    hvwTickets.Add(ticket);
                }
            }

            return (lvwTickets, hvwTickets);
        }

        /// <summary>
        /// Contains the core logic for shuffling tickets and writing all output report files.
        /// This method is called by both the "One-Click" and the manual "Combined Files" workflows.
        /// </summary>
        private async Task FinalizeGenerationAndWriteReports(ScratchCardProject project, List<Ticket> lvwTickets, List<Ticket> hvwTickets, string jobDirectory)
        {
            var gmcDir = Path.Combine(jobDirectory, "GMC");
            var gameFilesDir = Path.Combine(jobDirectory, "Game Files");
            var settings = project.Settings;
            Directory.CreateDirectory(gameFilesDir);

            _updateStatusCallback?.Invoke("Saving intermediate data files for auditing...");
            string lvwPath = Path.Combine(gameFilesDir, $"{settings.JobCode}-lvw.json");
            string hvwPath = Path.Combine(gameFilesDir, $"{settings.JobCode}-hvw.json");
            File.WriteAllText(lvwPath, JsonSerializer.Serialize(lvwTickets, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(hvwPath, JsonSerializer.Serialize(hvwTickets, new JsonSerializerOptions { WriteIndented = true }));

            var allTickets = lvwTickets.Concat(hvwTickets).ToList();
            int totalPrintTickets = settings.TotalTickets;

            _updateStatusCallback?.Invoke("Shuffling tickets into final print positions...");
            var artktPos = new int[totalPrintTickets + 1];
            var arLoser = new int[totalPrintTickets + 1];
            var arPackPQty = new byte[settings.PrintPacks + 1];
            var artktShuf = Enumerable.Range(0, settings.CardsPerPack).Select(i => (byte)i).ToArray();

            int ticketReportCount = (settings.NoComPack > 0) ? (int)Math.Ceiling((double)settings.PrintPacks / settings.NoComPack) : 0;
            for (int rpt = 1; rpt <= ticketReportCount; rpt++)
            {
                for (int pack = 1; pack <= settings.NoComPack; pack++)
                {
                    Shuffle(artktShuf, _random);
                    for (int tinpack = 1; tinpack <= settings.CardsPerPack; tinpack++)
                    {
                        int tpos = ((rpt - 1) * settings.TotalCommonTickets) + ((pack - 1) * settings.CardsPerPack) + tinpack;
                        if (tpos > totalPrintTickets) continue;
                        int fposn = ((pack - 1) * settings.CardsPerPack) + artktShuf[tinpack - 1];
                        artktPos[tpos] = fposn;
                        if (fposn < lvwTickets.Count && lvwTickets[fposn].WinPrize.Value > 0) arLoser[tpos] = 1;
                    }
                }
            }

            _updateStatusCallback?.Invoke("Placing high-value winners into the print run...");
            var availableLoserSlots = new List<int>();
            int totalLiveTickets = settings.TotalPacks * settings.CardsPerPack;
            for (int i = 1; i <= totalLiveTickets; i++) if (i < arLoser.Length && arLoser[i] == 0) availableLoserSlots.Add(i);
            Shuffle(availableLoserSlots, _random);

            int hvwIndex = 0;
            foreach (var hvwTicket in hvwTickets)
            {
                bool placed = false;
                for (int i = 0; i < availableLoserSlots.Count; i++)
                {
                    int tpos = availableLoserSlots[i];
                    int rpack = (tpos - 1) / settings.CardsPerPack + 1;
                    if (rpack < arPackPQty.Length && arPackPQty[rpack] < 2)
                    {
                        artktPos[tpos] = lvwTickets.Count + hvwIndex;
                        arPackPQty[rpack]++;
                        availableLoserSlots.RemoveAt(i);
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                {
                    throw new ApplicationException("Error: Could not find a suitable empty slot to place a high-value winner. Check HVW counts vs. total loser tickets.");
                }
                hvwIndex++;
            }

            var finalPrintRunTickets = new List<Ticket>();
            for (int i = 1; i <= totalPrintTickets; i++)
            {
                if (i < artktPos.Length && artktPos[i] < allTickets.Count)
                {
                    finalPrintRunTickets.Add(allTickets[artktPos[i]]);
                }
            }
            _progressReporter?.Report(50);

            _updateStatusCallback?.Invoke("Writing final report files...");
            string baseFileName = $"{settings.JobNo}-{settings.JobCode}";
            string combinedJsonPath = Path.Combine(gmcDir, $"{baseFileName}-combined.json");

            File.WriteAllText(combinedJsonPath, JsonSerializer.Serialize(finalPrintRunTickets, new JsonSerializerOptions { WriteIndented = true }));
            _progressReporter?.Report(60);

            WriteMainCsvReport(Path.Combine(gmcDir, $"{baseFileName}.csv"), project, allTickets, artktPos);
            _progressReporter?.Report(70);

            WriteLastPackCsv(gmcDir, project, allTickets, artktPos);
            WriteSummaryCsvReport(Path.Combine(gmcDir, "Game_Summary.csv"), project);
            WriteSamplesCsv(Path.Combine(gmcDir, $"{settings.JobNo}-{settings.JobCode}-SAMPLES.csv"), project, lvwTickets);
            WriteSetupCsv(Path.Combine(gmcDir, $"{settings.JobCode}-SETUP.csv"), project, lvwTickets);
            _progressReporter?.Report(71);

            if (!project.Settings.IsPoundlandGame)
            {
                WriteCustVerCsv(Path.Combine(gmcDir, $"{settings.JobCode}-CUSTVER.csv"), project, allTickets);
                WriteSimplePrizeBarcodeCsv(Path.Combine(gmcDir, $"{settings.JobCode}-PrizeBarcodes.csv"), project);
                WriteCombinedBarcodesTxt(Path.Combine(gmcDir, $"{settings.JobCode}-PrizeBarcodes.txt"), project);
                WriteSecurityRedemptionCsv(Path.Combine(gmcDir, $"{settings.JobCode}-Redemption Codes.csv"), project, allTickets, artktPos);
            }
            else
            {
                WritePoundlandRedemptionFile(Path.Combine(gmcDir, $"{settings.JobCode}Redemption Codes.txt"), project, allTickets, artktPos);
            }
            _progressReporter?.Report(95);

            _updateStatusCallback?.Invoke("Creating data integrity manifest...");
            string redemptionFilePath;
            if (project.Settings.IsPoundlandGame)
            {
                redemptionFilePath = Path.Combine(gmcDir, $"{settings.JobCode}Redemption Codes.txt");
            }
            else
            {
                redemptionFilePath = Path.Combine(gmcDir, $"{settings.JobCode}-Redemption Codes.csv");
            }
            var filesToHash = new List<string>
            {
                lvwPath, hvwPath, combinedJsonPath,
                Path.Combine(gmcDir, $"{baseFileName}.csv"),
                redemptionFilePath
            };
            string manifestPath = Path.Combine(gmcDir, "data_manifest.sha256");
            await _manifestService.CreateManifestFileAsync(filesToHash, manifestPath);
            _updateStatusCallback?.Invoke("Data integrity manifest created.");
            _progressReporter?.Report(100);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Generates a single, unique, and validated ticket for a given prize tier.
        /// </summary>
        private Ticket GenerateSingleTicket(PrizeTier prize, ScratchCardProject project, HashSet<string> uniqueFingerprints, int ticketIndexForError)
        {
            Ticket ticket;
            string fingerprint;
            int attempts = 0;

            do
            {
                ticket = new Ticket { WinPrize = prize };
                GameModule winningModule = SelectWinningModule(project, ticket.WinPrize, _random);
                foreach (var module in project.Layout.GameModules)
                {
                    bool isWinningModule = (module == winningModule);
                    module.GeneratePlayData(ticket, isWinningModule, ticket.WinPrize, project, _random);
                }
                fingerprint = GenerateTicketFingerprint(ticket);
                attempts++;
            } while (!uniqueFingerprints.Add(fingerprint) && attempts < 1000);

            if (attempts >= 1000)
            {
                throw new InvalidOperationException("Failed to generate a unique ticket after 1000 attempts. The symbol/game variety might be too low.");
            }

            var validationErrors = new List<string>();
            if (!_winValidator.ValidateTicketWin(ticket, project, ticketIndexForError, ref validationErrors))
            {
                string errorMsg = $"A critical internal error occurred. A generated ticket failed self-validation.\n\nErrors:\n- {string.Join("\n- ", validationErrors)}";
                throw new ApplicationException(errorMsg);
            }
            return ticket;
        }

        #region Generation Logic

        /// <summary>
        /// Selects an appropriate game module to be the winner for a given prize tier based on a set of rules.
        /// </summary>
        private GameModule SelectWinningModule(ScratchCardProject project, PrizeTier winPrize, Random random)
        {
            if (winPrize.Value <= 0 || !project.Layout.GameModules.Any()) return null;
            // Rule 1: Online prizes must be won on an OnlineBonusGame module.
            if (winPrize.IsOnlinePrize) return project.Layout.GameModules.OfType<OnlineBonusGame>().FirstOrDefault();

            // Rule 2: A prize can be linked to a specific MatchSymbolToPrizeGame via its TextCode.
            var specificWinnerModule = project.Layout.GameModules
                .OfType<MatchSymbolToPrizeGame>()
                .FirstOrDefault(g =>
                {
                    var symbol = project.NumericSymbols.FirstOrDefault(s => s.Id == g.WinningSymbolId);
                    if (symbol == null) return false;
                    var gamesPrize = project.PrizeTiers.FirstOrDefault(p => p.TextCode == symbol.Name && !p.IsOnlinePrize);
                    return gamesPrize?.Id == winPrize.Id;
                });

            if (specificWinnerModule != null) return specificWinnerModule;

            // Rule 3: If no specific module is required, choose a random "generic" module.
            var genericWinnableModules = project.Layout.GameModules
                .Where(m => !(m is OnlineBonusGame) && !(m is MatchSymbolToPrizeGame))
                .ToList();

            if (genericWinnableModules.Any()) return genericWinnableModules[random.Next(genericWinnableModules.Count)];

            // If no suitable module is found, return null.
            return null;
        }

        /// <summary>
        /// Creates a string "fingerprint" of a ticket's game data. This is used to check for and prevent duplicate tickets.
        /// </summary>
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
        /// Shuffles the elements of a generic list using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle<T>(List<T> list, Random random)
        {
            int n = list.Count;
            while (n > 1) { n--; int k = random.Next(n + 1); (list[k], list[n]) = (list[n], list[k]); }
        }

        /// <summary>
        /// Shuffles the elements of a byte array using the Fisher-Yates algorithm.
        /// </summary>
        private void Shuffle(byte[] array, Random random)
        {
            int n = array.Length;
            while (n > 1) { n--; int k = random.Next(n + 1); (array[k], array[n]) = (array[n], array[k]); }
        }

        #endregion

        #region File I/O

        /// <summary>
        /// Prompts the user to select a JSON ticket data file and deserialises it.
        /// </summary>
        private List<Ticket> ReadTicketFile(string title, string initialDirectory)
        {
            List<Ticket> tickets = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "JSON Ticket File (*.json)|*.json",
                    Title = title,
                    InitialDirectory = initialDirectory
                };

                if (ofd.ShowDialog() == true)
                {
                    try
                    {
                        _updateStatusCallback?.Invoke($"Reading file: {Path.GetFileName(ofd.FileName)}...");
                        string jsonString = File.ReadAllText(ofd.FileName);
                        tickets = JsonSerializer.Deserialize<List<Ticket>>(jsonString) ?? new List<Ticket>();
                        _updateStatusCallback?.Invoke("File read successfully.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error reading or deserialising file '{Path.GetFileName(ofd.FileName)}'.\n{ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
            return tickets;
        }

        /// <summary>
        /// Prepares the main job directory and its required subdirectories ("Game Files", "GMC").
        /// </summary>
        private string PrepareAndGetJobDirectory(ScratchCardProject project)
        {
            if (string.IsNullOrEmpty(project.Settings.JobNo) || string.IsNullOrEmpty(project.Settings.JobCode) || string.IsNullOrEmpty(project.Settings.JobName))
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Job Number, Job Code, and Job Name must all be set to determine the job directory.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error));
                return null;
            }

            string jobDirectory = Path.Combine(@"\\harlow.local\DFS\Gaming_Jobs", project.Settings.JobNo, $"{project.Settings.JobCode} - {project.Settings.JobName}");

            if (!Directory.Exists(jobDirectory))
            {
                var result = MessageBox.Show($"The job directory does not exist:\n\n{jobDirectory}\n\nWould you like to create it?", "Create Directory?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                {
                    return null;
                }
            }

            try
            {
                Directory.CreateDirectory(jobDirectory);
                Directory.CreateDirectory(Path.Combine(jobDirectory, "Game Files"));
                Directory.CreateDirectory(Path.Combine(jobDirectory, "GMC"));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Could not create job directories. Please check permissions.\n\nError: {ex.Message}", "Directory Creation Error", MessageBoxButton.OK, MessageBoxImage.Error));
                return null;
            }

            return jobDirectory;
        }

        #endregion

        #region Report Writing

        /// <summary>
        /// Generates the full CSV header string based on the project's game modules and settings.
        /// </summary>
        private string GetCsvHeader(ScratchCardProject project)
        {
            var orderedModules = project.Layout.GameModules.OrderBy(m => m.GameNumber).ToList();
            var header = new StringBuilder();
            foreach (var module in orderedModules)
            {
                header.Append(string.Join(",", module.GetCsvHeaders()));
                header.Append(",");
            }

            if (project.Settings.IsPoundlandGame)
            {
                header.Append("BarCode Number,Prize Amount,WinPrize,PrintOrder,PackInfo,BlockInfo,Info");
            }
            else
            {
                header.Append("WinPrize,WinGame,BarCode,ProdBarCode,PrintOrder,PackInfo,BlockInfo,Info");
            }
            return header.ToString();
        }

        /// <summary>
        /// Generates a single data row string for the main CSV report based on a ticket and its print order.
        /// </summary>
        private string GenerateCsvRow(int printOrder, Ticket ticket, ScratchCardProject project)
        {
            var orderedModules = project.Layout.GameModules.OrderBy(m => m.GameNumber).ToList();
            var settings = project.Settings;
            var line = new StringBuilder();

            foreach (var module in orderedModules)
            {
                if (module is OnlineBonusGame onlineGame)
                {
                    string securityCode = GetSecurityCode(printOrder, project);
                    string baseUrl = onlineGame.Url ?? string.Empty;
                    line.Append($"\"{baseUrl}\",");
                    line.Append($"\"{securityCode}\",");
                }
                else
                {
                    line.Append(string.Join(",", module.GetCsvRowData(ticket, project)));
                    line.Append(",");
                }
            }

            if (settings.IsPoundlandGame)
            {
                (int sec3, int sec7) = GetSecurityCodeParts(printOrder, project);
                string poundlandBarcode = GetPoundlandBarcode(sec3, sec7, project);
                int winPrizeIndex = ticket.WinPrize.Value == 0 ? 0 : project.PrizeTiers.ToList().FindIndex(p => p.Value == ticket.WinPrize.Value && p.IsOnlinePrize == ticket.WinPrize.IsOnlinePrize);
                line.Append($"{poundlandBarcode},{ticket.WinPrize.Value:F2},{winPrizeIndex},");
            }
            else
            {
                int winPrizeIndex = project.PrizeTiers.ToList().FindIndex(p => p.Value == ticket.WinPrize.Value && p.IsOnlinePrize == ticket.WinPrize.IsOnlinePrize);
                var winningGame = ticket.GameData.FirstOrDefault(g => g.PrizeTierIndex >= 0);
                int winGameFlag = winningGame?.GameNumber ?? 0;
                line.Append($"{winPrizeIndex},{winGameFlag},\"{ticket.WinPrize.Barcode}\",\"{settings.ProductBarcode}\",");
            }

            line.Append($"{printOrder},{settings.JobCode}-{(printOrder - 1) / settings.CardsPerPack + 1:D4}-{(printOrder - 1) % settings.CardsPerPack + 1:D3},");
            line.Append($"{(printOrder - 1) % settings.CardsPerPack + 1},\"{settings.JobNo} {settings.JobCode} {settings.JobName}\"");

            return line.ToString();
        }

        /// <summary>
        /// Writes the main, detailed CSV report for the entire print run.
        /// </summary>
        private void WriteMainCsvReport(string filePath, ScratchCardProject project, List<Ticket> allTickets, int[] artktPos)
        {
            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine(GetCsvHeader(project));
                for (int i = 1; i <= project.Settings.TotalTickets; i++)
                {
                    if (i >= artktPos.Length) continue;
                    int ticketIndex = artktPos[i];
                    if (ticketIndex >= allTickets.Count) continue;

                    var ticket = allTickets[ticketIndex];
                    sw.WriteLine(GenerateCsvRow(i, ticket, project));
                }
            }
        }

        /// <summary>
        /// For Poundland games, writes a separate CSV file containing only the tickets from the last pack.
        /// </summary>
        private void WriteLastPackCsv(string outputDir, ScratchCardProject project, List<Ticket> allTickets, int[] artktPos)
        {
            if (!project.Settings.IsPoundlandGame) return;

            var settings = project.Settings;
            int packSize = settings.CardsPerPack;
            int totalTickets = settings.TotalTickets;

            if (packSize <= 0 || packSize > totalTickets) return;

            string filePath = Path.Combine(outputDir, $"{settings.JobCode}_LastBlock.csv");

            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine(GetCsvHeader(project));
                int startTicket = totalTickets - packSize + 1;
                for (int i = startTicket; i <= totalTickets; i++)
                {
                    if (i >= artktPos.Length) continue;
                    int ticketIndex = artktPos[i];
                    if (ticketIndex >= allTickets.Count) continue;

                    var ticket = allTickets[ticketIndex];
                    sw.WriteLine(GenerateCsvRow(i, ticket, project));
                }
            }
        }

        /// <summary>
        /// Writes a special SETUP.csv file containing a small number of tickets with placeholder
        /// data, as required by the legacy workflow.
        /// </summary>
        private void WriteSetupCsv(string filePath, ScratchCardProject project, List<Ticket> lvwTickets)
        {
            var settings = project.Settings;
            // The setup file is only created if No. Out is greater than zero.
            if (settings.NoOut <= 0) return;

            // The setup file is built from the first "No. Out" loser tickets.
            var loserTickets = lvwTickets.Where(t => t.WinPrize.Value == 0).Take(settings.NoOut).ToList();
            if (!loserTickets.Any()) return;

            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine(GetCsvHeader(project));

                for (int i = 0; i < loserTickets.Count; i++)
                {
                    var ticket = loserTickets[i];
                    var line = new StringBuilder();

                    // This loop generates a special row with placeholder data, mimicking the VB logic.
                    foreach (var module in project.Layout.GameModules.OrderBy(m => m.GameNumber))
                    {
                        var playData = ticket.GameData.FirstOrDefault(g => g.GameNumber == module.GameNumber);
                        if (playData != null)
                        {
                            // A special rule from the VB code: Game 2 and 3 in the setup file have placeholders.
                            // We will approximate this by checking the module name. This may need adjustment
                            // if your game names are different.
                            if (module.ModuleName.Contains("Game 2") || module.ModuleName.Contains("Game 3"))
                            {
                                // This is a simplified placeholder logic.
                                // We will just write empty fields for now.
                                var headers = module.GetCsvHeaders();
                                for (int h = 0; h < headers.Count; h++)
                                {
                                    line.Append("\"\",");
                                }
                            }
                            else
                            {
                                line.Append(string.Join(",", module.GetCsvRowData(ticket, project)));
                                line.Append(",");
                            }
                        }
                    }

                    // Append the final info columns with "SETUP" text and a placeholder barcode.
                    string jobCodePart = settings.JobCode.Length >= 8 ? settings.JobCode.Substring(4, 4) : "0000";
                    string barcode = $"31{jobCodePart}0000000000";
                    line.Append($"{barcode},0.00,0,{i + 1},{settings.JobCode}-SETUP,SETUP,{settings.JobNo}-{settings.JobCode} SETUP");

                    sw.WriteLine(line.ToString());
                }
            }
        }

        /// <summary>
        /// Writes a CSV file containing sample tickets.
        /// </summary>
        private void WriteSamplesCsv(string filePath, ScratchCardProject project, List<Ticket> lvwTickets)
        {
            var settings = project.Settings;
            if (settings.NoOut <= 0) return;

            var loserTicketPool = lvwTickets.Where(t => t.WinPrize.Value == 0).Take(settings.NoOut).ToList();
            if (!loserTicketPool.Any()) return;

            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine(GetCsvHeader(project));

                for (int pack = 1; pack <= settings.NoOut; pack++)
                {
                    for (int tick = 1; tick <= settings.CardsPerPack; tick++)
                    {
                        var ticket = loserTicketPool[(tick - 1) % loserTicketPool.Count];
                        string ticketData = GenerateCsvRow(tick + ((pack - 1) * settings.CardsPerPack), ticket, project);

                        int lastComma = ticketData.LastIndexOf(',');
                        lastComma = ticketData.LastIndexOf(',', lastComma - 1);
                        lastComma = ticketData.LastIndexOf(',', lastComma - 1);
                        string baseData = ticketData.Substring(0, lastComma);

                        sw.WriteLine($"{baseData},{tick + ((pack - 1) * settings.CardsPerPack)},SAMPLES,SAMPLES,\"{settings.JobCode}-SAMPLES\"");
                    }
                }
            }
        }


        /// <summary>
        /// Writes a CSV file for customer verification.
        /// </summary>
        private void WriteCustVerCsv(string filePath, ScratchCardProject project, List<Ticket> allTickets)
        {
            var settings = project.Settings;
            var ticketsToWrite = new List<Ticket>();

            var winnerSamples = allTickets
                .Where(t => t.WinPrize.Value > 0 && !t.WinPrize.IsOnlinePrize)
                .GroupBy(t => t.WinPrize.Id)
                .Select(g => g.First())
                .OrderByDescending(t => t.WinPrize.Value)
                .ToList();

            ticketsToWrite.AddRange(winnerSamples);

            int winnerCount = ticketsToWrite.Count;
            int noOut = settings.NoOut > 0 ? settings.NoOut : 1;
            int targetSize = (int)Math.Ceiling((double)winnerCount / noOut) * noOut;
            if (targetSize == 0 && noOut > 0)
            {
                targetSize = noOut;
            }

            int losersNeeded = targetSize - winnerCount;
            if (losersNeeded > 0)
            {
                var loserSamples = allTickets.Where(t => t.WinPrize.Value == 0).Take(losersNeeded).ToList();
                ticketsToWrite.AddRange(loserSamples);
            }

            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine(GetCsvHeader(project));
                int printOrder = 1;
                foreach (var ticket in ticketsToWrite)
                {
                    string ticketData = GenerateCsvRow(printOrder, ticket, project);
                    int lastComma = ticketData.LastIndexOf(',');
                    lastComma = ticketData.LastIndexOf(',', lastComma - 1);
                    lastComma = ticketData.LastIndexOf(',', lastComma - 1);
                    string baseData = ticketData.Substring(0, lastComma);
                    sw.WriteLine($"{baseData},{printOrder},VERIFY_WINNERS,VERIFY_WINNERS,\"{settings.JobCode}-VERIFY_WINNERS\"");
                    printOrder++;
                }
            }
        }

        /// <summary>
        /// Writes a simple CSV containing only the barcodes of winning prize tiers.
        /// </summary>
        private void WriteSimplePrizeBarcodeCsv(string filePath, ScratchCardProject project)
        {
            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine("BarCode,Wintype,Prize Amount,EndDate");
                foreach (var prize in project.PrizeTiers.Where(p => p.Value > 0).OrderByDescending(p => p.Value))
                {
                    sw.WriteLine($"\"{prize.Barcode}\",WIN,{prize.Value:F2},{project.Settings.EndDate:dd/MM/yyyy}");
                }
            }
        }

        /// <summary>
        /// Writes a text file containing all barcodes for various production purposes.
        /// </summary>
        private void WriteCombinedBarcodesTxt(string filePath, ScratchCardProject project)
        {
            var settings = project.Settings;
            var barcodes = new List<(string Barcode, string Prize, string Artwork, string Type, string RievesRef)>();
            string artwork = settings.JobName;
            string rievesRef = settings.JobCode;

            barcodes.Add((settings.ProductBarcode, "Product Barcode", artwork, "", rievesRef));

            var winnablePrizes = project.PrizeTiers
                .Where(p => p.Value > 0 && (p.LvwWinnerCount > 0 || p.HvwWinnerCount > 0))
                .GroupBy(p => p.Barcode)
                .Select(g => g.First())
                .OrderByDescending(p => p.Value);

            foreach (var prize in winnablePrizes)
            {
                if (!string.IsNullOrWhiteSpace(prize.Barcode))
                {
                    barcodes.Add((prize.Barcode, $"{prize.DisplayText} - Prize Barcode", artwork, prize.IsOnlinePrize ? "ONLINE" : "OFFLINE", rievesRef));
                }
            }

            string loserBarcode = settings.LoserBarcode;
            if (!string.IsNullOrWhiteSpace(loserBarcode))
            {
                barcodes.Add((loserBarcode, "Losing Card - Prize Barcode", artwork, "ONLINE", rievesRef));
                barcodes.Add((loserBarcode, "Losing Card - Prize Barcode", artwork, "OFFLINE", rievesRef));
            }

            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine("Barcode,Prize,Artwork,Type,RievesRef");
                foreach (var b in barcodes)
                {
                    sw.WriteLine($"{b.Barcode},{b.Prize},{b.Artwork},{b.Type},{b.RievesRef}");
                }
            }
        }

        /// <summary>
        /// Writes the standard security redemption file.
        /// </summary>
        private void WriteSecurityRedemptionCsv(string filePath, ScratchCardProject project, List<Ticket> allTickets, int[] artktPos)
        {
            var onlineBonusGame = project.Layout.GameModules.OfType<OnlineBonusGame>().FirstOrDefault();
            string baseUrl = onlineBonusGame?.Url;

            try
            {
                _updateStatusCallback?.Invoke("Generating redemption codes file...");
                using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    sw.WriteLine("QRCode,Barcode,WinType,PrizeAmount,EndDate,URL");

                    for (int i = 1; i <= project.Settings.TotalTickets; i++)
                    {
                        if (i >= artktPos.Length) continue;
                        int ticketIndex = artktPos[i];
                        if (ticketIndex >= allTickets.Count) continue;

                        var ticket = allTickets[ticketIndex];
                        string securityCode = GetSecurityCode(i, project);
                        string fullUrl = !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(securityCode) ? $"{baseUrl}?code={securityCode}" : "";
                        string winType = ticket.WinPrize.Value == 0 ? "0" : (ticket.WinPrize.IsOnlinePrize ? "2" : "1");

                        sw.WriteLine($"\"{securityCode}\",\"{ticket.WinPrize.Barcode}\",\"{winType}\",{ticket.WinPrize.Value:F2},{project.Settings.EndDate:dd/MM/yyyy},\"{fullUrl}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred while generating the redemption codes file: {ex.Message}", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        /// <summary>
        /// Writes the special Poundland redemption file.
        /// </summary>
        private void WritePoundlandRedemptionFile(string filePath, ScratchCardProject project, List<Ticket> allTickets, int[] artktPos)
        {
            try
            {
                _updateStatusCallback?.Invoke("Generating Poundland redemption file...");
                using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    sw.WriteLine("BarCode Number,Prize Amount");

                    for (int i = 1; i <= project.Settings.TotalTickets; i++)
                    {
                        if (i >= artktPos.Length) continue;
                        int ticketIndex = artktPos[i];
                        if (ticketIndex >= allTickets.Count) continue;

                        var ticket = allTickets[ticketIndex];
                        (int sec3, int sec7) = GetSecurityCodeParts(i, project);
                        string poundlandBarcode = GetPoundlandBarcode(sec3, sec7, project);

                        sw.WriteLine($"{poundlandBarcode},{ticket.WinPrize.Value:F2}");
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"An error occurred while generating the Poundland redemption file: {ex.Message}", "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        /// <summary>
        /// Writes a high-level summary report of the game's prize structure and odds.
        /// </summary>
        private void WriteSummaryCsvReport(string filePath, ScratchCardProject project)
        {
            var settings = project.Settings;
            var prizes = project.PrizeTiers;
            using (var sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine("Category,Value,Note");
                sw.WriteLine();
                sw.WriteLine("--- Game Information ---,,");
                sw.WriteLine($"Job:,\"{settings.JobNo} - {settings.JobName}\",");
                sw.WriteLine($"Date:,{DateTime.Now:dd/MM/yyyy HH:mm:ss},");
                sw.WriteLine();

                long totalLvwWinnersInPacks = prizes.Where(p => p.Value > 0).Sum(p => (long)p.LvwWinnerCount);
                long totalLivePacks = settings.TotalPacks;
                long totalLiveLvwWinners = totalLvwWinnersInPacks * totalLivePacks;
                long totalHvwWinners = prizes.Sum(p => (long)p.HvwWinnerCount);
                long totalLiveWinners = totalLiveLvwWinners + totalHvwWinners;
                long totalLiveTickets = (long)settings.TotalPacks * settings.CardsPerPack;
                decimal lvwPrizeFundPerPack = prizes.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount * (decimal)p.Value);
                decimal totalLvwPrizeFund = lvwPrizeFundPerPack * totalLivePacks;
                decimal totalHvwPrizeFund = prizes.Sum(p => p.HvwWinnerCount * (decimal)p.Value);
                decimal totalPrizeFund = totalLvwPrizeFund + totalHvwPrizeFund;
                decimal totalSales = (long)settings.TotalTickets * settings.TicketSalePrice;
                decimal payoutPercentage = totalSales > 0 ? (totalPrizeFund / totalSales) * 100 : 0;
                string printRunNote = "(Note: Payout % is based on total print run sales, including extras for setup/spares.)";

                sw.WriteLine("--- Ticket Breakdown (Live Packs) ---,,");
                sw.WriteLine($"Total Live Tickets:,\"{totalLiveTickets:N0}\",");
                sw.WriteLine($"Total Winning Tickets:,\"{totalLiveWinners:N0}\",");
                sw.WriteLine($"Total Losing Tickets:,\"{(totalLiveTickets - totalLiveWinners):N0}\",");
                if (totalLiveWinners > 0) sw.WriteLine($"Overall Odds:,\"1 in {(double)totalLiveTickets / totalLiveWinners:F2}\",");
                else sw.WriteLine("Overall Odds:,N/A,");
                sw.WriteLine();
                sw.WriteLine("--- Financials ---,,");
                sw.WriteLine($"Total Prize Fund (Live Packs):,\"{totalPrizeFund:C}\",");
                sw.WriteLine($"Total Potential Sales (Print Run):,\"{totalSales:C}\",");
                sw.WriteLine($"Payout Percentage:,\"{payoutPercentage:F2}%\",\"{printRunNote}\"");
                sw.WriteLine();
                sw.WriteLine("--- Low-Value Winner Breakdown (Per Pack) ---,,");
                sw.WriteLine("Prize,Winners Per Pack,");
                foreach (var prize in prizes.Where(p => p.Value > 0 && p.LvwWinnerCount > 0).OrderByDescending(p => p.Value))
                {
                    sw.WriteLine($"\"{prize.DisplayText}\",{prize.LvwWinnerCount},");
                }
                sw.WriteLine();
                sw.WriteLine("--- High-Value Winner Breakdown (Total Job) ---,,");
                sw.WriteLine("Prize,Total Winners,");
                foreach (var prize in prizes.Where(p => p.HvwWinnerCount > 0).OrderByDescending(p => p.Value))
                {
                    sw.WriteLine($"\"{prize.DisplayText}\",{prize.HvwWinnerCount},");
                }
            }
        }

        #endregion

        #region Security

        /// <summary>
        /// Opens a stream to a binary security file (.rnd) for on-demand reading.
        /// </summary>
        private void OpenSecurityCodeFile(string filePath, out BinaryReader reader, out long count)
        {
            reader = null;
            count = 0;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            try
            {
                var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                reader = new BinaryReader(fs);
                count = fs.Length / sizeof(int);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Failed to open security file: {Path.GetFileName(filePath)}\n\nError: {ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error));
                reader?.Dispose();
                reader = null;
                count = 0;
            }
        }

        /// <summary>
        /// Opens file streams for all specified security code files.
        /// </summary>
        private void LoadSecurityFiles(ScratchCardProject project)
        {
            _updateStatusCallback?.Invoke("Loading security files...");
            OpenSecurityCodeFile(project.Security.SixDigitCodeFilePath, out _reader6Digit, out _count6Digit);
            OpenSecurityCodeFile(project.Security.ThreeDigitCodeFilePath, out _reader3Digit, out _count3Digit);
            OpenSecurityCodeFile(project.Security.SevenDigitCodeFilePath, out _reader7Digit, out _count7Digit);
            _updateStatusCallback?.Invoke("Security files loaded.");
        }

        /// <summary>
        /// Safely closes and disposes all open security file streams.
        /// </summary>
        private void CloseSecurityFiles()
        {
            _reader3Digit?.Dispose();
            _reader6Digit?.Dispose();
            _reader7Digit?.Dispose();
            _reader3Digit = null;
            _reader6Digit = null;
            _reader7Digit = null;
        }

        /// <summary>
        /// Retrieves a single security code for a specific ticket by reading from the open file streams.
        /// </summary>
        private string GetSecurityCode(int printOrder, ScratchCardProject project)
        {
            if (_reader6Digit != null && _count6Digit > 0)
            {
                string prefix = project.Settings.JobCode.Length >= 3 ? project.Settings.JobCode.Substring(project.Settings.JobCode.Length - 3) : "???";
                long position = ((long)printOrder - 1) % _count6Digit;
                _reader6Digit.BaseStream.Seek(position * sizeof(int), SeekOrigin.Begin);
                int sec6 = _reader6Digit.ReadInt32();
                return $"{prefix}{sec6:D6}";
            }

            if ((_reader3Digit != null && _count3Digit > 0) && (_reader7Digit != null && _count7Digit > 0))
            {
                (int sec3, int sec7) = GetSecurityCodeParts(printOrder, project);
                return $"{project.Settings.JobNo}{sec3:D3}{printOrder:D6}{sec7:D7}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Retrieves the raw 3-digit and 7-digit security codes for a specific ticket.
        /// </summary>
        private (int sec3, int sec7) GetSecurityCodeParts(int printOrder, ScratchCardProject project)
        {
            int sec3 = 0;
            int sec7 = 0;

            if ((_reader3Digit != null && _count3Digit > 0) && (_reader7Digit != null && _count7Digit > 0))
            {
                long pos3 = ((long)printOrder - 1) % _count3Digit;
                _reader3Digit.BaseStream.Seek(pos3 * sizeof(int), SeekOrigin.Begin);
                sec3 = _reader3Digit.ReadInt32();

                long pos7 = ((long)printOrder - 1) % _count7Digit;
                _reader7Digit.BaseStream.Seek(pos7 * sizeof(int), SeekOrigin.Begin);
                sec7 = _reader7Digit.ReadInt32();
            }
            return (sec3, sec7);
        }

        /// <summary>
        /// Generates the special Poundland barcode based on the defined formula.
        /// </summary>
        private string GetPoundlandBarcode(int sec3, int sec7, ScratchCardProject project)
        {
            string prefix = project.Settings.PoundlandBarcodePrefix;

            // MODIFIED: This logic now uses the last 3 digits of the Job Code.
            string jobCodePart = project.Settings.JobCode.Length >= 3 ? project.Settings.JobCode.Substring(project.Settings.JobCode.Length - 3) : "000";

            // The legacy VB code concatenated the security codes as strings, not mathematical addition.
            return $"{prefix}{jobCodePart}{sec3}{sec7}";
        }


        #endregion

        #region PDF Generation

        /// <summary>
        /// A helper method for the PDF generator to create a single row containing a barcode and its description.
        /// </summary>
        private void AddBarcodeRow(IContainer container, PrizeTier prize, bool? isOnline)
        {
            if (string.IsNullOrWhiteSpace(prize.Barcode)) return;
            try
            {
                var b = new Barcode { IncludeLabel = true, };
                SKImage barcodeImage = b.Encode(BarcodeStandard.Type.Ean13, prize.Barcode, SKColors.Black, SKColors.White, 300, 100);

                byte[] barcodeImageData;
                using (var imageStream = new MemoryStream())
                {
                    barcodeImage.Encode(SKEncodedImageFormat.Png, 100).SaveTo(imageStream);
                    barcodeImageData = imageStream.ToArray();
                }

                container.Row(row =>
                {
                    row.ConstantItem(90).Image(barcodeImageData);
                    row.RelativeItem().PaddingLeft(10).Column(col =>
                    {
                        col.Item().Text($"{prize.DisplayText}").SemiBold();
                        col.Item().Text(prize.Barcode);
                    });
                    if (isOnline.HasValue)
                    {
                        row.ConstantItem(100).AlignRight().Text(isOnline.Value ? "ONLINE" : "OFFLINE").Bold();
                    }
                });
            }
            catch (Exception)
            {
                // Silently ignore barcodes that fail to generate to prevent the whole PDF from failing.
            }
        }

        #endregion

        #endregion
    }
}