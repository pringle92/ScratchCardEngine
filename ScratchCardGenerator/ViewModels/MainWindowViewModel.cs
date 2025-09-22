#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using Microsoft.Win32;
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.Services;
using ScratchCardGenerator.Common.ViewModels;
using ScratchCardGenerator.Common.ViewModels.Commands;
using ScratchCardGenerator.Services;
using ScratchCardGenerator.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

#endregion

namespace ScratchCardGenerator.ViewModels
{
    /// <summary>
    /// Serves as the primary ViewModel for the application's main window (MainWindow.xaml).
    /// This class orchestrates all application-level concerns, acting as the central hub that connects
    /// the user interface with the underlying services and data models.
    /// </summary>
    /// <remarks>
    /// Its responsibilities include:
    /// - Handling all file operations (New, Save, Load, Templates).
    /// - Managing the list of recently opened files.
    /// - Implementing the auto-save and crash recovery mechanism.
    /// - Initiating all file generation and analysis tasks by delegating to the appropriate services.
    /// - Managing the state of the UI, such as status messages, progress indicators, and the window title.
    /// - Hosting the child <see cref="ProjectEditorViewModel"/> in a composition pattern.
    /// </remarks>
    public class MainWindowViewModel : ViewModelBase
    {
        #region Private Fields

        /// <summary>
        /// A reference to the service responsible for all file generation logic. This follows the principle of
        /// dependency inversion, where this ViewModel depends on an abstraction of the service's functionality.
        /// </summary>
        private readonly FileGenerationService _fileGenerationService;

        /// <summary>
        /// A reference to the service responsible for analysing generated files.
        /// </summary>
        private readonly FileAnalysisService _fileAnalysisService;

        /// <summary>
        /// The filename for the default project that is created or loaded on startup.
        /// </summary>
        private const string DefaultProjectFileName = "default.scproj";

        /// <summary>
        /// The private backing field for the <see cref="StatusText"/> property.
        /// </summary>
        private string _statusText = "Ready";

        /// <summary>
        /// A timer used to reset the status bar text back to a default state after a few seconds,
        /// preventing transient messages from persisting indefinitely.
        /// </summary>
        private DispatcherTimer _statusResetTimer;

        /// <summary>
        /// The private backing field for the <see cref="ProgressValue"/> property.
        /// </summary>
        private int _progressValue;

        /// <summary>
        /// The private backing field for the <see cref="IsProgressVisible"/> property.
        /// </summary>
        private bool _isProgressVisible;

        /// <summary>
        /// A timer that triggers the auto-save functionality at a regular interval to prevent data loss.
        /// </summary>
        private DispatcherTimer _autoSaveTimer;

        /// <summary>
        /// The filename for the temporary auto-save/recovery file. This file is created in the system's temp directory.
        /// </summary>
        private const string AutoSaveFileName = "scg_autosave.tmp";

        /// <summary>
        /// The filename for the JSON file that stores the list of recently opened projects, providing user convenience.
        /// </summary>
        private const string RecentFilesFileName = "scg_recent.json";

        /// <summary>
        /// The maximum number of files to show in the "Recent Files" menu.
        /// </summary>
        private const int MaxRecentFiles = 10;

        /// <summary>
        /// Provides access to the Undo/Redo service, which manages the history of changes made to the project.
        /// </summary>
        private readonly UndoRedoService _undoRedoService;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the dedicated ViewModel for the project editor control. This is an example of ViewModel composition,
        /// where this main ViewModel hosts and manages a child ViewModel, promoting modularity and separation of concerns.
        /// </summary>
        public ProjectEditorViewModel EditorViewModel { get; }

        /// <summary>
        /// A pass-through property to get the current project from the editor's ViewModel for convenience in binding and logic.
        /// </summary>
        public ScratchCardProject CurrentProject => EditorViewModel.CurrentProject;

        /// <summary>
        /// Gets or sets the text displayed in the application's status bar, providing real-time feedback to the user.
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the title for the main window. It dynamically includes the current project's filename
        /// and an asterisk (*) to indicate unsaved changes, providing clear visual feedback to the user.
        /// </summary>
        public string WindowTitle => $"Scratch Card Generator - {(string.IsNullOrEmpty(CurrentProject?.FilePath) ? "Untitled" : Path.GetFileName(CurrentProject.FilePath))}{(EditorViewModel.IsDirty ? "*" : "")}";

        /// <summary>
        /// Gets the current value for the progress bar (0-100), used during long-running operations like file generation.
        /// </summary>
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the progress bar should be visible in the status bar.
        /// </summary>
        public bool IsProgressVisible
        {
            get => _isProgressVisible;
            set { _isProgressVisible = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the collection of recently opened file paths, which is bound to the "Recent Files" menu item in the UI.
        /// </summary>
        public ObservableCollection<string> RecentFiles { get; private set; }

        #endregion

        #region Commands

        // Defines all ICommand properties that the main window's menus and toolbars can bind to.
        // This decouples the View (e.g., a Button) from the action's implementation in the ViewModel, a core tenet of MVVM.
        public ICommand NewProjectCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand SaveProjectAsCommand { get; private set; }
        public ICommand LoadProjectCommand { get; private set; }
        public ICommand SaveAsTemplateCommand { get; private set; }
        public ICommand NewFromTemplateCommand { get; private set; }
        public ICommand RunFullGenerationCommand { get; private set; }
        public ICommand GenerateLvwCommand { get; private set; }
        public ICommand GenerateHvwCommand { get; private set; }
        public ICommand GenerateCombinedFileCommand { get; private set; }
        public ICommand CheckLvwCommand { get; private set; }
        public ICommand CheckHvwCommand { get; private set; }
        public ICommand PrintBarcodesCommand { get; private set; }
        public ICommand ShowAboutWindowCommand { get; private set; }
        public ICommand ShowHelpGuideCommand { get; private set; }
        public ICommand ExitCommand { get; private set; }
        public ICommand OpenRecentFileCommand { get; private set; }
        public ICommand ExportPrizesCommand { get; private set; }
        public ICommand ImportPrizesCommand { get; private set; }
        public ICommand ExportSymbolsCommand { get; private set; }
        public ICommand ImportSymbolsCommand { get; private set; }
        public ICommand ShowAdvancedAnalysisCommand { get; private set; }
        public ICommand OpenJobFolderCommand { get; private set; }
        public ICommand UndoCommand { get; private set; }
        public ICommand RedoCommand { get; private set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="MainWindowViewModel"/> class.
        /// The constructor is responsible for setting up the entire application's logical state.
        /// </summary>
        public MainWindowViewModel()
        {
            RecentFiles = new ObservableCollection<string>();

            // Set up the callback and progress reporter that will be passed to the services.
            // This allows the services to communicate back to this ViewModel without having a direct reference to it.
            Action<string> updateStatusCallback = (message) => SetStatus(message);
            var progress = new Progress<int>(value => ProgressValue = value);

            // Instantiate the services the ViewModel depends on (Dependency Injection).
            _fileGenerationService = new FileGenerationService(updateStatusCallback, progress);
            _fileAnalysisService = new FileAnalysisService(updateStatusCallback);

            // NEW: Initialise the UndoRedoService. There will be only one instance for the entire application.
            _undoRedoService = new UndoRedoService();

            // Instantiate the child ViewModel for the editor.
            EditorViewModel = new ProjectEditorViewModel(_undoRedoService);
            // Subscribe to the child's PropertyChanged event to know when to update the main window title.
            EditorViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ProjectEditorViewModel.IsDirty))
                {
                    OnPropertyChanged(nameof(WindowTitle));
                }
            };

            // Set up all application commands.
            InitialiseCommands();
            InitialiseStatusResetTimer();

            // On startup, check if there's an auto-save file to recover from a previous crash.
            bool recovered = CheckForRecovery();
            if (!recovered)
            {
                // If no recovery was needed, load the default project template.
                LoadOrCreateDefaultProject();
            }

            // Load the list of recent files from disk and initialise the auto-save timer.
            LoadRecentFilesList();
            InitialiseAutoSaveTimer();
        }

        #endregion

        #region Command Initialisation

        /// <summary>
        /// Initialises all <see cref="ICommand"/> properties with their corresponding implementations and CanExecute logic.
        /// </summary>
        private void InitialiseCommands()
        {
            // File Operations
            NewProjectCommand = new RelayCommand(p => NewProject());
            SaveProjectCommand = new RelayCommand(p => SaveProject(), p => CurrentProject != null && !string.IsNullOrEmpty(CurrentProject.FilePath));
            SaveProjectAsCommand = new RelayCommand(p => SaveProjectAs(), p => CurrentProject != null);
            LoadProjectCommand = new RelayCommand(p => LoadProject());
            SaveAsTemplateCommand = new RelayCommand(p => SaveAsTemplate(), p => CurrentProject != null);
            NewFromTemplateCommand = new RelayCommand(p => NewFromTemplate());
            OpenRecentFileCommand = new RelayCommand(OpenRecentFile);

            RunFullGenerationCommand = new AsyncRelayCommand(async () =>
            {
                if (!CanProceedWithGeneration()) return;
                IsProgressVisible = true;
                await _fileGenerationService.RunFullGenerationAsync(CurrentProject);
                IsProgressVisible = false;
            }, () => CurrentProject != null);

            // Generation and Analysis (using AsyncRelayCommand for long-running tasks to keep the UI responsive)
            // MODIFIED: The generation commands now call the CanProceedWithGeneration helper method.
            GenerateLvwCommand = new AsyncRelayCommand(async () =>
            {
                if (!CanProceedWithGeneration()) return;
                IsProgressVisible = true;
                await _fileGenerationService.GenerateLvwFile(CurrentProject);
                IsProgressVisible = false;
            }, () => CurrentProject != null);

            GenerateHvwCommand = new AsyncRelayCommand(async () =>
            {
                if (!CanProceedWithGeneration()) return;
                IsProgressVisible = true;
                await _fileGenerationService.GenerateHvwFile(CurrentProject);
                IsProgressVisible = false;
            }, () => CurrentProject != null);

            GenerateCombinedFileCommand = new AsyncRelayCommand(async () =>
            {
                if (!CanProceedWithGeneration()) return;

                if (CurrentProject.Settings.IsPoundlandGame)
                {
                    if (string.IsNullOrWhiteSpace(CurrentProject.Security.ThreeDigitCodeFilePath) || string.IsNullOrWhiteSpace(CurrentProject.Security.SevenDigitCodeFilePath))
                    {
                        MessageBox.Show("Poundland game generation requires both a 3-Digit and a 7-Digit security file to be specified in the Security Settings.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                IsProgressVisible = true;
                await _fileGenerationService.GenerateCombinedFile(CurrentProject);
                IsProgressVisible = false;
            }, () => CurrentProject != null);

            CheckLvwCommand = new RelayCommand(p => _fileAnalysisService.CheckLvwFile(CurrentProject), p => CurrentProject != null);
            CheckHvwCommand = new RelayCommand(p => _fileAnalysisService.CheckHvwFile(CurrentProject), p => CurrentProject != null);
            PrintBarcodesCommand = new AsyncRelayCommand(async () =>
            {
                if (CurrentProject.Settings.IsPoundlandGame)
                {
                    MessageBox.Show("The 'Print Barcodes' feature is not applicable to Poundland games, as barcodes are generated uniquely for each ticket.", "Feature Not Applicable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                await _fileGenerationService.PrintBarcodesPdf(CurrentProject);
            }, () => CurrentProject != null);

            // Window and Help Operations
            ShowAboutWindowCommand = new RelayCommand(ShowAboutWindow);
            ShowHelpGuideCommand = new RelayCommand(ShowHelpGuide);
            ExitCommand = new RelayCommand(p => Application.Current.MainWindow.Close());
            ShowAdvancedAnalysisCommand = new RelayCommand(ShowAdvancedAnalysis, p => CurrentProject != null);
            OpenJobFolderCommand = new RelayCommand(OpenJobFolder, CanOpenJobFolder);

            // NEW: Initialise the Undo/Redo commands.
            // The 'execute' action calls the service's method.
            // The 'canExecute' predicate is bound to the service's property, so the UI will
            // automatically enable/disable the menu items.
            UndoCommand = new RelayCommand(p => _undoRedoService.Undo(), p => _undoRedoService.CanUndo);
            RedoCommand = new RelayCommand(p => _undoRedoService.Redo(), p => _undoRedoService.CanRedo);

            // Import/Export (delegated to the child ViewModel)
            ExportPrizesCommand = new RelayCommand(p => EditorViewModel.ExportPrizesCommand.Execute(p));
            ImportPrizesCommand = new RelayCommand(p => EditorViewModel.ImportPrizesCommand.Execute(p));
            ExportSymbolsCommand = new RelayCommand(p => EditorViewModel.ExportSymbolsCommand.Execute(p));
            ImportSymbolsCommand = new RelayCommand(p => EditorViewModel.ImportSymbolsCommand.Execute(p));
        }

        #endregion

        #region NEW: Generation Pre-Flight Check

        /// <summary>
        /// Checks the project for critical validation errors before allowing generation to proceed.
        /// If errors are found, it prompts the user for confirmation.
        /// </summary>
        /// <returns>True if generation should proceed; otherwise, false.</returns>
        private bool CanProceedWithGeneration()
        {
            // Force an up-to-date validation check to ensure we have the latest issues.
            EditorViewModel.RunValidation();

            // Filter for critical errors only; warnings are informational and should not block this prompt.
            var errors = EditorViewModel.ValidationIssues
                .Where(i => i.Severity == IssueSeverity.Error)
                .ToList();

            // If there are no errors, we can proceed without any user interaction.
            if (!errors.Any())
            {
                return true;
            }

            // --- If errors exist, build a message and prompt the user ---
            var messageBuilder = new StringBuilder();
            messageBuilder.AppendLine("Your project has critical errors that will likely cause generation to fail:");
            messageBuilder.AppendLine();
            foreach (var error in errors)
            {
                messageBuilder.AppendLine($"• {error.Message}");
            }
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("Are you sure you want to continue with file generation anyway?");

            // Show the confirmation dialog to the user.
            var result = MessageBox.Show(
                messageBuilder.ToString(),
                "Project Validation Errors",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            // Only proceed if the user explicitly clicks "Yes".
            return result == MessageBoxResult.Yes;
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Command handler to create a new, empty project, prompting to save existing work first.
        /// </summary>
        private void NewProject()
        {
            if (PromptToSaveChanges())
            {
                EditorViewModel.CurrentProject = CreateNewDefaultProject();
                EditorViewModel.IsDirty = false;
            }
        }

        /// <summary>
        /// Command handler to save the current project to its existing file path, or trigger "Save As" if no path exists.
        /// </summary>
        /// <returns>True if the save was successful or not needed; false if the user cancelled.</returns>
        private bool SaveProject()
        {
            if (CurrentProject == null) return false;
            // If the project already has a file path, save to it directly.
            if (!string.IsNullOrEmpty(CurrentProject.FilePath))
            {
                PerformSave(CurrentProject.FilePath);
                return true;
            }
            // Otherwise, delegate to the SaveProjectAs logic.
            return SaveProjectAs();
        }

        /// <summary>
        /// Command handler to save the current project to a new file path chosen by the user.
        /// </summary>
        /// <returns>True if the save was successful; false if the user cancelled.</returns>
        private bool SaveProjectAs()
        {
            if (CurrentProject == null) return false;
            var sfd = new SaveFileDialog
            {
                Filter = "Scratch Card Project (*.scproj)|*.scproj|All Files (*.*)|*.*",
                Title = "Save Project As",
                FileName = $"{CurrentProject.Settings.JobCode} - {CurrentProject.Settings.JobName}.scproj"
            };
            if (sfd.ShowDialog() == true)
            {
                CurrentProject.FilePath = sfd.FileName;
                PerformSave(CurrentProject.FilePath);
                AddToRecentFiles(sfd.FileName);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Performs the actual file write operation for saving a project by serialising the project object to JSON.
        /// </summary>
        /// <param name="filePath">The path to save the file to.</param>
        private void PerformSave(string filePath)
        {
            SetStatus($"Saving project to {Path.GetFileName(filePath)}...");
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(CurrentProject, options);
                File.WriteAllText(filePath, jsonString);

                // After a successful save, the project is no longer "dirty".
                EditorViewModel.IsDirty = false;
                (SaveProjectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(WindowTitle));
                SetStatus("Project saved successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Error saving project.");
            }
        }

        /// <summary>
        /// Command handler to load a project from a file, prompting to save existing work first.
        /// </summary>
        private void LoadProject()
        {
            if (!PromptToSaveChanges()) return;
            SetStatus("Loading project...");
            var ofd = new OpenFileDialog
            {
                Filter = "Scratch Card Project (*.scproj)|*.scproj|All Files (*.*)|*.*",
                Title = "Load Project"
            };
            if (ofd.ShowDialog() == true)
            {
                LoadProjectFromFile(ofd.FileName);
            }
            else
            {
                SetStatus("Load operation cancelled.");
            }
        }

        /// <summary>
        /// Performs the file read and deserialization for loading a project.
        /// </summary>
        /// <param name="filePath">The path of the project file to load.</param>
        private void LoadProjectFromFile(string filePath)
        {
            try
            {
                string jsonString = File.ReadAllText(filePath);
                var loadedProject = JsonSerializer.Deserialize<ScratchCardProject>(jsonString);
                if (loadedProject != null)
                {
                    // Run a helper to ensure backwards compatibility with older project files.
                    AssignMissingPrizeTierIds(loadedProject);
                    loadedProject.FilePath = filePath;
                    EditorViewModel.CurrentProject = loadedProject;
                    SetStatus($"Project '{Path.GetFileName(filePath)}' loaded successfully.");
                    EditorViewModel.IsDirty = false;
                    AddToRecentFiles(filePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Error loading project.");
            }
        }

        /// <summary>
        /// Prompts the user to save any unsaved changes before proceeding with an action that would discard them.
        /// </summary>
        /// <returns>True if the action should proceed (user saved or chose not to); false if the user cancelled the action.</returns>
        public bool PromptToSaveChanges()
        {
            if (!EditorViewModel.IsDirty) return true;

            var result = MessageBox.Show(
                "You have unsaved changes. Would you like to save them before continuing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    return SaveProject(); // Proceed only if the save was successful.
                case MessageBoxResult.No:
                    return true; // Proceed without saving.
                case MessageBoxResult.Cancel:
                    return false; // Cancel the original action.
                default:
                    return false;
            }
        }

        #endregion

        #region Template Methods

        /// <summary>
        /// Creates and saves the current project state as a sanitised, reusable template.
        /// This involves cloning the project and resetting all job-specific data.
        /// </summary>
        private void SaveAsTemplate()
        {
            var sfd = new SaveFileDialog
            {
                Filter = "Scratch Card Template (*.sctemplate)|*.sctemplate",
                Title = "Save Project as Template",
                FileName = $"{CurrentProject.Settings.Client} Template.sctemplate"
            };

            if (sfd.ShowDialog() == true)
            {
                StatusText = "Creating template...";
                try
                {
                    // --- Create a Deep Clone ---
                    // It is critical to work on a copy of the project, not the live one.
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(CurrentProject);
                    var templateProject = JsonSerializer.Deserialize<ScratchCardProject>(json);

                    // --- Sanitise the Template ---
                    // Reset job-specific information to defaults.
                    templateProject.FilePath = null;
                    templateProject.Settings.JobNo = "000000";
                    templateProject.Settings.JobName = "New Job";
                    templateProject.Settings.JobCode = "NEW00001";
                    templateProject.Settings.TotalPacks = 1;
                    templateProject.Settings.NoComPack = 1;

                    // Reset all prize counts and barcodes.
                    foreach (var prize in templateProject.PrizeTiers)
                    {
                        if (prize.Value > 0)
                        {
                            prize.LvwWinnerCount = 0;
                            prize.HvwWinnerCount = 0;
                            prize.Barcode = string.Empty;
                        }
                    }

                    // Clear security file paths.
                    templateProject.Security.SixDigitCodeFilePath = null;
                    templateProject.Security.ThreeDigitCodeFilePath = null;
                    templateProject.Security.SevenDigitCodeFilePath = null;

                    // --- Save the Sanitised Template File ---
                    string templateJson = JsonSerializer.Serialize(templateProject, options);
                    File.WriteAllText(sfd.FileName, templateJson);
                    SetStatus("Project template saved successfully.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not save template: {ex.Message}", "Template Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetStatus("Error saving template.");
                }
            }
        }

        /// <summary>
        /// Starts a new project by loading a sanitised template file.
        /// </summary>
        private void NewFromTemplate()
        {
            if (!PromptToSaveChanges()) return;

            var ofd = new OpenFileDialog
            {
                Filter = "Scratch Card Template (*.sctemplate)|*.sctemplate",
                Title = "New Project from Template"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    StatusText = "Loading template...";
                    string jsonString = File.ReadAllText(ofd.FileName);
                    var loadedProject = JsonSerializer.Deserialize<ScratchCardProject>(jsonString);

                    if (loadedProject != null)
                    {
                        EditorViewModel.CurrentProject = loadedProject;
                        EditorViewModel.IsDirty = false;
                        StatusText = $"New project created from '{Path.GetFileName(ofd.FileName)}'.";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not load template file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText = "Error loading template.";
                }
            }
        }

        #endregion

        #region Auto-Save and Recovery

        /// <summary>
        /// Initialises the DispatcherTimer responsible for triggering the auto-save process every two minutes.
        /// </summary>
        private void InitialiseAutoSaveTimer()
        {
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }

        /// <summary>
        /// The event handler for the auto-save timer's Tick event. It runs the auto-save in a background thread.
        /// </summary>
        private async void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            // Only perform an auto-save if there are unsaved changes.
            if (EditorViewModel.IsDirty)
            {
                await Task.Run(() => PerformAutoSave());
            }
        }

        /// <summary>
        /// Performs the auto-save operation by serialising the current project to a temporary file.
        /// Includes robust error handling to prevent crashes and log failures.
        /// </summary>
        private void PerformAutoSave()
        {
            try
            {
                string autoSavePath = Path.Combine(Path.GetTempPath(), AutoSaveFileName);
                var options = new JsonSerializerOptions { WriteIndented = false }; // No need for indentation in a temp file.
                string jsonString = JsonSerializer.Serialize(CurrentProject, options);
                File.WriteAllText(autoSavePath, jsonString);
            }
            catch (Exception ex)
            {
                // If auto-save fails, we attempt to log the error to a file in AppData
                // without crashing the main application.
                try
                {
                    string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string appFolderPath = Path.Combine(appDataPath, "ScratchCardGenerator");
                    Directory.CreateDirectory(appFolderPath);
                    string logFilePath = Path.Combine(appFolderPath, "scg_autosave_error_log.txt");

                    var logContent = new StringBuilder();
                    logContent.AppendLine($"--- Auto-Save Error on {DateTime.Now:G} ---");
                    logContent.AppendLine($"Message: {ex.Message}");
                    logContent.AppendLine("StackTrace:");
                    logContent.AppendLine(ex.StackTrace);
                    logContent.AppendLine("--------------------------------------------------");
                    logContent.AppendLine();

                    File.AppendAllText(logFilePath, logContent.ToString());
                }
                catch (Exception)
                {
                    // If logging itself fails, there is nothing more we can do. Fail silently.
                }
            }
        }

        /// <summary>
        /// Checks for the existence of a recovery file on application startup and prompts the user to recover it.
        /// </summary>
        /// <returns>True if a project was successfully recovered; otherwise, false.</returns>
        private bool CheckForRecovery()
        {
            string autoSavePath = Path.Combine(Path.GetTempPath(), AutoSaveFileName);
            if (File.Exists(autoSavePath))
            {
                var result = MessageBox.Show(
                    "The application appears to have closed unexpectedly.\n\nWould you like to recover the last auto-saved project?",
                    "Recover Project?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        string jsonString = File.ReadAllText(autoSavePath);
                        var recoveredProject = JsonSerializer.Deserialize<ScratchCardProject>(jsonString);
                        if (recoveredProject != null)
                        {
                            AssignMissingPrizeTierIds(recoveredProject);
                            EditorViewModel.CurrentProject = recoveredProject;
                            EditorViewModel.IsDirty = true; // The recovered project is considered "dirty" to prompt a save.
                            SetStatus("Project recovered successfully. Please save your work.");
                            File.Delete(autoSavePath); // Clean up the recovery file.
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not recover the auto-saved project.\n\nError: {ex.Message}", "Recovery Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // If the user declines recovery, delete the file.
                    File.Delete(autoSavePath);
                }
            }
            return false;
        }

        /// <summary>
        /// Cleans up the auto-save file during a normal application exit.
        /// </summary>
        public void CleanUpAutoSaveFile()
        {
            try
            {
                string autoSavePath = Path.Combine(Path.GetTempPath(), AutoSaveFileName);
                if (File.Exists(autoSavePath)) File.Delete(autoSavePath);
            }
            catch (Exception) { /* Fails silently as it's not a critical error on exit. */ }
        }

        #endregion

        #region Recent Files Logic

        /// <summary>
        /// Adds a file path to the top of the recent files list, ensuring no duplicates and respecting the maximum count.
        /// </summary>
        /// <param name="filePath">The full path of the file to add.</param>
        private void AddToRecentFiles(string filePath)
        {
            if (RecentFiles.Contains(filePath)) RecentFiles.Remove(filePath);
            RecentFiles.Insert(0, filePath);
            while (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveAt(RecentFiles.Count - 1);
            SaveRecentFilesList();
        }

        /// <summary>
        /// Gets the full path to the file used for storing the recent files list in the user's AppData folder.
        /// </summary>
        private string GetRecentFilesPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "ScratchCardGenerator");
            Directory.CreateDirectory(appFolderPath); // Ensure the directory exists.
            return Path.Combine(appFolderPath, RecentFilesFileName);
        }

        /// <summary>
        /// Loads the recent files list from its JSON file.
        /// </summary>
        private void LoadRecentFilesList()
        {
            string recentFilesPath = GetRecentFilesPath();
            if (!File.Exists(recentFilesPath)) return;
            try
            {
                string json = File.ReadAllText(recentFilesPath);
                var files = JsonSerializer.Deserialize<List<string>>(json);
                if (files != null)
                {
                    RecentFiles.Clear();
                    foreach (var file in files) RecentFiles.Add(file);
                }
            }
            catch (Exception) { /* Fail silently if the file is corrupt. */ }
        }

        /// <summary>
        /// Saves the current recent files list to its JSON file.
        /// </summary>
        private void SaveRecentFilesList()
        {
            try
            {
                string recentFilesPath = GetRecentFilesPath();
                string json = JsonSerializer.Serialize(RecentFiles);
                File.WriteAllText(recentFilesPath, json);
            }
            catch (Exception) { /* Fail silently. */ }
        }

        /// <summary>
        /// Command handler for opening a project from the recent files list.
        /// </summary>
        /// <param name="parameter">The file path string of the project to open.</param>
        private void OpenRecentFile(object parameter)
        {
            if (parameter is not string filePath || string.IsNullOrEmpty(filePath)) return;

            if (!File.Exists(filePath))
            {
                MessageBox.Show($"The file '{Path.GetFileName(filePath)}' could not be found.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                RecentFiles.Remove(filePath);
                SaveRecentFilesList();
                return;
            }

            if (PromptToSaveChanges())
            {
                LoadProjectFromFile(filePath);
            }
        }

        #endregion

        #region UI and Helper Methods

        /// <summary>
        /// Initialises the timer used to reset the status bar message after a short delay.
        /// </summary>
        private void InitialiseStatusResetTimer()
        {
            _statusResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _statusResetTimer.Tick += (sender, e) => { _statusResetTimer.Stop(); StatusText = "Ready"; };
        }

        /// <summary>
        /// Sets the status bar message. If the message indicates a final state (e.g., "complete", "saved"),
        /// it starts a timer to reset the message back to "Ready".
        /// </summary>
        /// <param name="message">The message to display.</param>
        private void SetStatus(string message)
        {
            _statusResetTimer.Stop();
            StatusText = message;
            // Check for keywords that indicate a final status message.
            if (message.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("saved", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                _statusResetTimer.Start();
            }
        }

        /// <summary>
        /// Command handler to show the About window.
        /// </summary>
        private void ShowAboutWindow(object parameter)
        {
            var aboutWindow = new AboutWindow { Owner = Application.Current.MainWindow };
            aboutWindow.ShowDialog();
        }

        /// <summary>
        /// Command handler to show the Help Guide window, ensuring only one instance is open at a time.
        /// </summary>
        private void ShowHelpGuide(object parameter)
        {
            // Check if a help window is already open and activate it if so.
            foreach (Window window in Application.Current.Windows)
            {
                if (window is HelpGuideWindow)
                {
                    window.Activate();
                    return;
                }
            }
            // If not open, create and show a new one.
            var helpWindow = new HelpGuideWindow { Owner = Application.Current.MainWindow };
            helpWindow.Show();
        }

        /// <summary>
        /// Command handler to show the Advanced Analysis window.
        /// </summary>
        private void ShowAdvancedAnalysis(object parameter)
        {
            var advancedViewModel = new AdvancedAnalysisViewModel(CurrentProject);
            var advancedWindow = new AdvancedAnalysisWindow
            {
                DataContext = advancedViewModel,
                Owner = Application.Current.MainWindow
            };
            advancedWindow.ShowDialog();
        }

        /// <summary>
        /// Predicate for the OpenJobFolderCommand, determining if the command can be executed.
        /// </summary>
        private bool CanOpenJobFolder(object parameter)
        {
            // The command can only execute if the necessary path components are defined.
            return CurrentProject != null &&
                   !string.IsNullOrWhiteSpace(CurrentProject.Settings.JobNo) &&
                   !string.IsNullOrWhiteSpace(CurrentProject.Settings.JobCode) &&
                   !string.IsNullOrWhiteSpace(CurrentProject.Settings.JobName);
        }

        /// <summary>
        /// Command handler to construct the job folder path and open it in Windows Explorer.
        /// </summary>
        private void OpenJobFolder(object parameter)
        {
            if (!CanOpenJobFolder(null))
            {
                MessageBox.Show("Job Number, Job Code, and Job Name must be set to locate the job folder.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string jobDirectory = Path.Combine(@"\\harlow.local\DFS\Gaming_Jobs", CurrentProject.Settings.JobNo, $"{CurrentProject.Settings.JobCode} - {CurrentProject.Settings.JobName}");

                if (Directory.Exists(jobDirectory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = jobDirectory,
                        UseShellExecute = true // UseShellExecute is required to open a folder in Explorer.
                    });
                }
                else
                {
                    MessageBox.Show($"The job directory could not be found at the expected location:\n\n{jobDirectory}", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while trying to open the job folder.\n\nError: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Project Initialisation

        /// <summary>
        /// Loads a default project from a file in the application directory, or creates and saves one if it doesn't exist.
        /// </summary>
        private void LoadOrCreateDefaultProject()
        {
            SetStatus("Loading default project...");
            string defaultProjectPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                DefaultProjectFileName);

            if (File.Exists(defaultProjectPath))
            {
                try
                {
                    string jsonString = File.ReadAllText(defaultProjectPath);
                    var loadedProject = JsonSerializer.Deserialize<ScratchCardProject>(jsonString);
                    if (loadedProject != null)
                    {
                        AssignMissingPrizeTierIds(loadedProject);
                        loadedProject.FilePath = null; // The default project is always "Untitled".
                        EditorViewModel.CurrentProject = loadedProject;
                        SetStatus("Default project loaded.");
                        EditorViewModel.IsDirty = false;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not load the default project file. A new default will be created.\n\nError: {ex.Message}", "Default Project Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // If loading fails or the file doesn't exist, create a new one.
            EditorViewModel.CurrentProject = CreateNewDefaultProject();
            EditorViewModel.IsDirty = false;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(CurrentProject, options);
                File.WriteAllText(defaultProjectPath, jsonString);
                SetStatus("New default project created.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the new default project file.\n\nError: {ex.Message}", "Default Project Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Error creating default project.");
            }
        }

        /// <summary>
        /// Creates a new <see cref="ScratchCardProject"/> instance populated with sensible default data for a new user.
        /// </summary>
        /// <returns>A new, pre-populated <see cref="ScratchCardProject"/>.</returns>
        private ScratchCardProject CreateNewDefaultProject()
        {
            SetStatus("Creating new project...");
            var project = new ScratchCardProject
            {
                Settings = new JobSettings
                {
                    JobNo = "000000",
                    JobName = "Default Project",
                    JobCode = "DEF00001",
                    Client = "Default Client",
                    ProductBarcode = "5000000000000",
                    LoserBarcode = "0700465541963",
                    CardsPerPack = 60,
                    NoOut = 0,
                    TotalPacks = 1000,
                    NoComPack = 0,
                    TicketSalePrice = 1.00m,
                    EndDate = DateTime.Now.AddYears(1)
                }
            };

            // Pre-populate with some common symbols.
            project.AvailableSymbols.Add(new Symbol { Id = 1, DisplayText = "STAR" });
            project.AvailableSymbols.Add(new Symbol { Id = 2, DisplayText = "SAFE" });
            project.AvailableSymbols.Add(new Symbol { Id = 3, DisplayText = "DIAMND" });
            project.AvailableSymbols.Add(new Symbol { Id = 4, DisplayText = "CROWN" });
            project.AvailableSymbols.Add(new Symbol { Id = 5, DisplayText = "STACK" });
            project.AvailableSymbols.Add(new Symbol { Id = 6, DisplayText = "PIGGY" });
            project.AvailableSymbols.Add(new Symbol { Id = 7, DisplayText = "WAD" });
            project.AvailableSymbols.Add(new Symbol { Id = 8, DisplayText = "GOLD" });
            project.AvailableSymbols.Add(new Symbol { Id = 9, DisplayText = "WALLET" });
            project.AvailableSymbols.Add(new Symbol { Id = 10, DisplayText = "CHMPG" });
            project.AvailableSymbols.Add(new Symbol { Id = 11, DisplayText = "CASH" });
            project.AvailableSymbols.Add(new Symbol { Id = 12, DisplayText = "CAR" });
            project.AvailableSymbols.Add(new Symbol { Id = 13, DisplayText = "RING" });

            // Pre-populate with some common numeric symbols.
            for (int i = 2; i <= 17; i++)
            {
                if (i == 13) continue; // Skip 13 for superstitious reasons, as in the legacy app.
                project.NumericSymbols.Add(new Symbol { Id = i, DisplayText = ConvertNumberToText(i) });
            }

            // Pre-populate with a standard prize structure.
            var defaultPrizes = new[]
            {
                new { Value = 1000, Text = "£1000", Code = "ONETHOU", Barcode = "0700465541956" },
                new { Value = 100, Text = "£100", Code = "ONEHUN", Barcode = "0700465541956" },
                new { Value = 75, Text = "£75", Code = "SVTYFV", Barcode = "0700465541956" },
                new { Value = 50, Text = "£50", Code = "FIFTY", Barcode = "0700465541949" },
                new { Value = 25, Text = "£25", Code = "TWNTYFV", Barcode = "0700465541895" },
                new { Value = 20, Text = "£20", Code = "TWNTY", Barcode = "0700465541888" },
                new { Value = 10, Text = "£10", Code = "TEN", Barcode = "0700465541826" },
                new { Value = 5, Text = "£5", Code = "FIVE", Barcode = "0700465541772" },
                new { Value = 4, Text = "£4", Code = "FOUR", Barcode = "0700465541765" },
                new { Value = 3, Text = "£3", Code = "THREE", Barcode = "0700465541758" },
                new { Value = 2, Text = "£2", Code = "TWO", Barcode = "0700465541741" },
                new { Value = 1, Text = "£1", Code = "ONE", Barcode = "0700465541734" }
            };

            int currentId = 1;
            foreach (var prizeInfo in defaultPrizes)
            {
                // For each value, create both an offline and an online prize tier.
                project.PrizeTiers.Add(new PrizeTier { Id = currentId++, Value = prizeInfo.Value, DisplayText = prizeInfo.Text, TextCode = prizeInfo.Code, Barcode = prizeInfo.Barcode, IsOnlinePrize = false });
                project.PrizeTiers.Add(new PrizeTier { Id = currentId++, Value = prizeInfo.Value, DisplayText = prizeInfo.Text, TextCode = prizeInfo.Code, Barcode = "", IsOnlinePrize = true });
            }

            SetStatus("Ready");
            return project;
        }

        /// <summary>
        /// A simple helper method to convert an integer to its English word representation for default symbol creation.
        /// </summary>
        private string ConvertNumberToText(int number)
        {
            switch (number)
            {
                case 1: return "ONE";
                case 2: return "TWO";
                case 3: return "THREE";
                case 4: return "FOUR";
                case 5: return "FIVE";
                case 6: return "SIX";
                case 7: return "SEVEN";
                case 8: return "EIGHT";
                case 9: return "NINE";
                case 10: return "TEN";
                case 11: return "ELEVEN";
                case 12: return "TWELVE";
                case 14: return "FOURTEEN";
                case 15: return "FIFTEEN";
                case 16: return "SIXTEEN";
                case 17: return "SEVENTEEN";
                default: return number.ToString();
            }
        }

        /// <summary>
        /// A data migration helper to ensure backward compatibility with older project files
        /// that may not have saved a unique 'Id' for each prize tier.
        /// </summary>
        /// <param name="project">The project to check and repair.</param>
        private void AssignMissingPrizeTierIds(ScratchCardProject project)
        {
            if (project.PrizeTiers.Any(p => p.Id == 0 && p.Value > 0))
            {
                int maxId = project.PrizeTiers.Any() ? project.PrizeTiers.Max(p => p.Id) : 0;
                foreach (var prize in project.PrizeTiers.Where(p => p.Id == 0 && p.Value > 0))
                {
                    prize.Id = ++maxId;
                }
            }
        }

        #endregion
    }
}