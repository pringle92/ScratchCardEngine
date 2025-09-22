#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using FinalGameChecker.Services;
using Microsoft.Win32;
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.Services;
using ScratchCardGenerator.Common.ViewModels;
using ScratchCardGenerator.Common.ViewModels.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

#endregion

namespace FinalGameChecker.ViewModels
{
    /// <summary>
    /// The main ViewModel for the Final Game Checker application.
    /// This class orchestrates the entire validation workflow, from loading files to running checks and reporting results.
    /// It follows the MVVM pattern, separating the application's logic from its user interface.
    /// </summary>
    public class CheckerViewModel : ViewModelBase
    {
        #region Private Fields

        // Services that encapsulate specific, complex functionalities, making the ViewModel cleaner.
        private readonly ProjectComparisonService _comparisonService;
        private readonly DataIntegrityService _integrityService;
        private readonly UndoRedoService _undoRedoService = new UndoRedoService();

        // Backing fields for public properties.
        private ScratchCardProject _originalProject;
        private string _originalProjectFilePath;
        private string _statusText;
        private string _overallStatus;
        private bool _hasDifferences;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the dedicated ViewModel for the project editor control.
        /// This is an example of ViewModel composition, reusing the editor from the common library.
        /// </summary>
        public ProjectEditorViewModel EditorViewModel { get; }

        /// <summary>
        /// Gets or sets the project that is manually created or loaded by the checker.
        /// This property acts as a pass-through to the hosted editor's ViewModel.
        /// </summary>
        public ScratchCardProject CheckerProject
        {
            get => EditorViewModel.CurrentProject;
            set => EditorViewModel.CurrentProject = value;
        }

        /// <summary>
        /// Gets or sets the project loaded from the original generator's output directory.
        /// This is the project that will be validated against the checker's project.
        /// </summary>
        public ScratchCardProject OriginalProject
        {
            get => _originalProject;
            set { _originalProject = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the file path of the loaded original project, for display in the UI.
        /// </summary>
        public string OriginalProjectFilePath
        {
            get => _originalProjectFilePath;
            set { _originalProjectFilePath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the title for the main application window.
        /// </summary>
        public string WindowTitle => "Final Game Checker";

        /// <summary>
        /// Gets or sets the text displayed in the status bar to inform the user of the current state.
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the final overall status of the validation (e.g., "PASS", "FAIL", "N/A").
        /// This property is bound to the UI to provide a clear, colour-coded result.
        /// </summary>
        public string OverallStatus
        {
            get => _overallStatus;
            set { _overallStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// An observable collection of structured difference objects found during project comparison.
        /// This is bound to the results ListView in the UI.
        /// </summary>
        public ObservableCollection<DifferenceViewModel> ComparisonDifferences { get; }

        /// <summary>
        /// Gets or sets a value indicating whether any differences were found between the two projects.
        /// This is used to control the visibility of UI elements like the "Show Visual Diff" button.
        /// </summary>
        public bool HasDifferences
        {
            get => _hasDifferences;
            set { _hasDifferences = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        // Defines all ICommand properties that the View can bind to for user actions.
        public ICommand SaveCheckerProjectCommand { get; }
        public ICommand LoadCheckerProjectCommand { get; }
        public ICommand LoadOriginalProjectCommand { get; }
        public ICommand RunChecksCommand { get; }
        public ICommand ShowDiffCommand { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="CheckerViewModel"/> class.
        /// </summary>
        public CheckerViewModel()
        {
            // Instantiate the services this ViewModel depends on.
            _comparisonService = new ProjectComparisonService();
            _integrityService = new DataIntegrityService();

            // Initialise collections and default property values.
            ComparisonDifferences = new ObservableCollection<DifferenceViewModel>();

            // Create an initial, empty project for the user to start with, pre-filling a common value.
            var initialProject = new ScratchCardProject();
            initialProject.Settings.LoserBarcode = "0700465541963";

            // Instantiate the dedicated ViewModel for the project editor control.
            EditorViewModel = new ProjectEditorViewModel(undoRedoService: new UndoRedoService())
            {
                // Start with the newly created project loaded in the editor.
                CurrentProject = initialProject
            };

            StatusText = "Ready. Please create or load a checker project to begin.";
            OverallStatus = "N/A";

            // Initialise all commands, linking them to their handler methods and specifying their CanExecute conditions.
            SaveCheckerProjectCommand = new RelayCommand(p => SaveProject(CheckerProject), p => CheckerProject != null);
            LoadCheckerProjectCommand = new RelayCommand(p => LoadCheckerProject());
            LoadOriginalProjectCommand = new RelayCommand(p => LoadOriginalProject());
            ShowDiffCommand = new RelayCommand(p => _comparisonService.LaunchVisualDiff(OriginalProject, CheckerProject), p => HasDifferences);
            RunChecksCommand = new AsyncRelayCommand(RunChecks, () => CheckerProject != null && OriginalProject != null);
        }

        #endregion

        #region Command Methods

        /// <summary>
        /// The main orchestration method that runs the entire validation workflow asynchronously.
        /// Using an async command ensures the UI remains responsive during this potentially long-running process.
        /// </summary>
        private async Task RunChecks()
        {
            // Reset UI state at the beginning of a run.
            ComparisonDifferences.Clear();
            HasDifferences = false;
            OverallStatus = "Checking...";

            try
            {
                // --- Step 1: Data Integrity Check ---
                StatusText = "Verifying data file integrity...";
                var (integrityPassed, loadedTickets, message) = await VerifyDataIntegrityAndLoadFiles();
                if (!integrityPassed)
                {
                    MessageBox.Show(message, "Data Integrity Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText = "Check aborted due to data integrity failure.";
                    OverallStatus = "FAIL";
                    return;
                }
                StatusText = "Data integrity check passed. Files loaded.";

                // --- Step 2: Project Comparison ---
                StatusText = "Comparing project files...";
                var (areEqual, differences) = _comparisonService.CompareProjects(OriginalProject, CheckerProject);

                if (!areEqual)
                {
                    HasDifferences = true;
                    foreach (var diff in differences)
                    {
                        ComparisonDifferences.Add(diff);
                    }
                }

                // --- Step 3: Execute Final Checks Service ---
                StatusText = "Running detailed checks on game data...";
                // Instantiate the main checking engine, passing in all required data.
                var finalChecks = new FinalChecks(
                    OriginalProject,
                    CheckerProject,
                    loadedTickets["LVW"],
                    loadedTickets["HVW"],
                    loadedTickets["Combined"],
                    OriginalProjectFilePath,
                    loadedTickets["LVW_Path"],
                    loadedTickets["HVW_Path"],
                    loadedTickets["Combined_Path"],
                    differences
                );

                // Execute the checks and generate the report.
                string reportPath = finalChecks.ExecuteAndGenerateReport(Path.GetDirectoryName(OriginalProjectFilePath));

                // --- Step 4: Report Final Status ---
                OverallStatus = finalChecks.OverallStatus;
                StatusText = $"Check complete. Overall Status: {OverallStatus}.";
                MessageBox.Show($"Validation checks are complete.\n\nA detailed PDF report has been saved to:\n{reportPath}", "Check Finished", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                StatusText = "A critical error occurred during the check.";
                OverallStatus = "ERROR";
                MessageBox.Show($"An unexpected error stopped the process: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region File Handling and Helper Methods

        /// <summary>
        /// Handles the logic for loading the manually created checker project file.
        /// </summary>
        private void LoadCheckerProject()
        {
            var ofd = new OpenFileDialog { Filter = "Checker Project File (*.scproj)|*.scproj", Title = "Load Checker Project" };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(ofd.FileName);
                    var loadedProject = JsonSerializer.Deserialize<ScratchCardProject>(json);

                    // Ensure the loaded project is in a valid state.
                    ValidateAndInitializeProject(loadedProject);

                    EditorViewModel.CurrentProject = loadedProject;
                    StatusText = "Checker project loaded successfully.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load checker project file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Handles the logic for loading the original project file from the generator's output.
        /// </summary>
        private void LoadOriginalProject()
        {
            var ofd = new OpenFileDialog { Filter = "Original Project File (*.scproj)|*.scproj", Title = "Load Original Generator Project" };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(ofd.FileName);
                    var loadedProject = JsonSerializer.Deserialize<ScratchCardProject>(json);

                    ValidateAndInitializeProject(loadedProject);

                    OriginalProject = loadedProject;
                    OriginalProjectFilePath = ofd.FileName;
                    StatusText = "Original project loaded. Ready to run checks.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load original project file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Handles the logic for saving the manually created checker project file.
        /// </summary>
        /// <param name="project">The project object to save.</param>
        private void SaveProject(ScratchCardProject project)
        {
            var sfd = new SaveFileDialog { Filter = "Checker Project File (*.scproj)|*.scproj", Title = "Save Checker Project" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(project, options);
                    File.WriteAllText(sfd.FileName, json);
                    StatusText = "Checker project saved successfully.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save checker project file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Verifies the integrity of required data files using the manifest and loads them if successful.
        /// </summary>
        /// <returns>A tuple containing a success flag, a dictionary of loaded ticket data, and an error message if applicable.</returns>
        private async Task<(bool Success, Dictionary<string, dynamic> LoadedData, string Message)> VerifyDataIntegrityAndLoadFiles()
        {
            string projectDir = Path.GetDirectoryName(OriginalProjectFilePath);
            string gmcDir = Path.Combine(projectDir, "GMC");
            string gameFilesDir = Path.Combine(projectDir, "Game Files");
            string manifestPath = Path.Combine(gmcDir, "data_manifest.sha256");

            if (!File.Exists(manifestPath))
            {
                return (false, null, "Data integrity manifest file (data_manifest.sha256) not found in GMC directory. Cannot verify file integrity.");
            }

            // Define the set of critical files that must be verified.
            string lvwPath = Path.Combine(gameFilesDir, $"{OriginalProject.Settings.JobCode}-lvw.json");
            string hvwPath = Path.Combine(gameFilesDir, $"{OriginalProject.Settings.JobCode}-hvw.json");
            string combinedPath = Path.Combine(gmcDir, $"{OriginalProject.Settings.JobNo}-{OriginalProject.Settings.JobCode}-combined.json");

            // MODIFIED: Conditionally determine the correct redemption file name based on the project type.
            string redemptionPath;
            if (OriginalProject.Settings.IsPoundlandGame)
            {
                redemptionPath = Path.Combine(gmcDir, $"{OriginalProject.Settings.JobCode}Redemption Codes.txt");
            }
            else
            {
                redemptionPath = Path.Combine(gmcDir, $"{OriginalProject.Settings.JobCode}-Redemption Codes.csv");
            }

            var filesToVerify = new Dictionary<string, string>
            {
                { Path.GetFileName(lvwPath), lvwPath },
                { Path.GetFileName(hvwPath), hvwPath },
                { Path.GetFileName(combinedPath), combinedPath },
                { Path.GetFileName(redemptionPath), redemptionPath } // Use the correct file path
            };

            // Delegate the verification logic to the DataIntegrityService.
            var (integrityPassed, mismatchedFiles) = await _integrityService.VerifyFilesAsync(manifestPath, filesToVerify);
            if (!integrityPassed)
            {
                return (false, null, $"The following files failed the integrity check or were missing from the manifest:\n\n- {string.Join("\n- ", mismatchedFiles)}");
            }

            // If integrity check passes, proceed to load the files.
            try
            {
                var loadedData = new Dictionary<string, dynamic>
                {
                    ["LVW"] = JsonSerializer.Deserialize<List<Ticket>>(File.ReadAllText(lvwPath)),
                    ["HVW"] = JsonSerializer.Deserialize<List<Ticket>>(File.ReadAllText(hvwPath)),
                    ["Combined"] = JsonSerializer.Deserialize<List<Ticket>>(File.ReadAllText(combinedPath)),
                    ["LVW_Path"] = lvwPath,
                    ["HVW_Path"] = hvwPath,
                    ["Combined_Path"] = combinedPath
                };
                return (true, loadedData, "Success");
            }
            catch (Exception ex)
            {
                return (false, null, $"Files passed integrity check, but failed to load or parse: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures that a deserialized project object has all its main properties initialised to prevent NullReferenceExceptions.
        /// This is important for robustness when loading files that might be missing certain collections.
        /// </summary>
        /// <param name="project">The project object to validate.</param>
        private void ValidateAndInitializeProject(ScratchCardProject project)
        {
            if (project == null) return;

            // Use the null-coalescing assignment operator (??=) for a concise way to initialise null properties.
            project.Settings ??= new JobSettings();
            project.Security ??= new SecuritySettings();
            project.PrizeTiers ??= new ObservableCollection<PrizeTier>();
            project.AvailableSymbols ??= new ObservableCollection<Symbol>();
            project.NumericSymbols ??= new ObservableCollection<Symbol>();
            project.Layout ??= new CardLayout();
            project.Layout.GameModules ??= new ObservableCollection<GameModule>();
        }

        #endregion
    }
}
