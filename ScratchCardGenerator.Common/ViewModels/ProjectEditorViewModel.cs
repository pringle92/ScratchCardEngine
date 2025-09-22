#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using Microsoft.VisualBasic;
using Microsoft.Win32;
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.Services;
using ScratchCardGenerator.Common.ViewModels.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

#endregion

namespace ScratchCardGenerator.Common.ViewModels
{
    /// <summary>
    /// A dedicated ViewModel that encapsulates all logic and data for the ProjectEditorControl.
    /// This class manages the state of the currently loaded ScratchCardProject, handles user interactions via commands,
    /// and performs live analysis of the game's prize structure. It acts as the central hub between the data models and the editor view.
    /// </summary>
    public class ProjectEditorViewModel : ViewModelBase
    {
        #region Private Fields

        /// <summary>
        /// The backing field for the currently active scratch card project being edited.
        /// </summary>
        private ScratchCardProject _currentProject;

        /// <summary>
        /// The backing field for the currently selected game module in the designer canvas.
        /// </summary>
        private GameModule _selectedModule;

        /// <summary>
        /// A flag to prevent re-entrant (i.e., recursive or overlapping) calls to the RecalculateAnalysis method.
        /// This is important because many properties can trigger a recalculation, and this prevents a cascade of redundant updates.
        /// </summary>
        private bool _isRecalculating = false;

        /// <summary>
        /// The backing field for the IsGamesAwareForcedOn property. This tracks whether the "Games Aware" feature is mandatory.
        /// </summary>
        private bool _isGamesAwareForcedOn;

        /// <summary>
        /// The backing field for the IsDirty property, which tracks whether the project has unsaved changes.
        /// </summary>
        private bool _isDirty;

        /// <summary>
        /// The backing field for the IsSixDigitSecurityEnabled property, controlling UI element state.
        /// </summary>
        private bool _isSixDigitSecurityEnabled = true;

        /// <summary>
        /// The backing field for the IsThreeAndSevenDigitSecurityEnabled property, controlling UI element state.
        /// </summary>
        private bool _isThreeAndSevenDigitSecurityEnabled = true;
        
        /// <summary>
        /// The service responsible for validating project data and ensuring it meets all requirements.
        /// </summary>
        private readonly ProjectValidationService _validationService;

        /// <summary>
        /// A timer used to debounce validation requests, preventing excessive calls to the validation service.
        /// </summary>
        private readonly DispatcherTimer _validationTimer;

        /// <summary>
        /// A flag indicating whether to show warnings in the UI.
        /// </summary>
        private bool _showWarnings = true;


        /// <summary>
        /// A service that provides undo/redo functionality for the project editor.
        /// </summary>
        private readonly UndoRedoService _undoRedoService;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the currently active scratch card project being edited.
        /// The setter is responsible for detaching event handlers from the old project and attaching them to the new one
        /// to ensure proper memory management and event subscription.
        /// </summary>
        public ScratchCardProject CurrentProject
        {
            get => _currentProject;
            set
            {
                // Detach handlers from the old project to prevent memory leaks and incorrect event firing.
                if (_currentProject != null) DetachProjectEventHandlers(_currentProject);

                _currentProject = value;

                // Attach handlers to the new project to monitor for changes that require UI updates or recalculations.
                if (_currentProject != null)
                {
                    AttachProjectEventHandlers(_currentProject);
                    EnsureLoserTierExists();

                    // Set up a filtered and sorted view for the prize tiers grid. This is a best practice in WPF
                    // for applying sorting/filtering/grouping logic without modifying the underlying source collection.
                    WinnablePrizeTiers = CollectionViewSource.GetDefaultView(CurrentProject.PrizeTiers);

                    // Filter out the automatic "NO WIN" tier so the user only sees editable prizes.
                    WinnablePrizeTiers.Filter = p => ((PrizeTier)p).Value > 0;

                    // Always sort the prizes by value in descending order for user convenience.
                    WinnablePrizeTiers.SortDescriptions.Clear();
                    WinnablePrizeTiers.SortDescriptions.Add(new SortDescription(nameof(PrizeTier.Value), ListSortDirection.Descending));
                    OnPropertyChanged(nameof(WinnablePrizeTiers));
                }
                OnPropertyChanged();

                // Update all dependent properties and states whenever a new project is assigned.
                RecalculateAnalysis();


                UpdateGamesAwareStatus();
                RenumberGameModules();
                UpdateSecurityControlStates();
                // Run validation immediately when a new project is loaded.
                RunValidation();
            }
        }

        /// <summary>
        /// Gets or sets the currently selected game module from the designer canvas.
        /// The setter manages the IsSelected visual state of the modules, ensuring only one is selected at a time.
        /// </summary>
        public GameModule SelectedModule
        {
            get => _selectedModule;
            set
            {
                // Deselect the previously selected module, if any, to remove its visual highlight.
                if (_selectedModule != null) _selectedModule.IsSelected = false;

                _selectedModule = value;

                // Select the new module, if any, to apply its visual highlight.
                if (_selectedModule != null) _selectedModule.IsSelected = true;

                OnPropertyChanged();

                // Notify commands that their executable status may have changed (e.g., Move Up/Down commands).
                (MoveModuleUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (MoveModuleDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Gets a view of the PrizeTiers collection that is filtered to exclude the "NO WIN" tier and sorted.
        /// This is bound to the main prize grid in the UI, providing a clean, user-friendly view of the data.
        /// </summary>
        public ICollectionView WinnablePrizeTiers { get; private set; }

        /// <summary>
        /// Gets the collection of available game modules that can be dragged from the palette onto the designer.
        /// This is a static list of all possible game types the user can add to a card layout.
        /// </summary>
        public ObservableCollection<GameModule> AvailableGameModules { get; }

        /// <summary>
        /// NEW: A filtered view of the ValidationIssues collection. The UI will bind to this.
        /// </summary>
        public ICollectionView FilteredValidationIssues { get; }

        /// <summary>
        /// NEW: A property bound to the "Show Warnings" checkbox in the UI.
        /// </summary>
        public bool ShowWarnings
        {
            get => _showWarnings;
            set
            {
                _showWarnings = value;
                OnPropertyChanged();
                // When this value changes, we must refresh the collection view to re-apply the filter.
                FilteredValidationIssues.Refresh();
            }
        }

        /// <summary>
        /// Gets a value indicating whether the "Games Aware" feature is forced on due to project configuration.
        /// This is used to disable the checkbox in the UI, providing clear feedback to the user about mandatory settings.
        /// </summary>
        public bool IsGamesAwareForcedOn
        {
            get => _isGamesAwareForcedOn;
            private set { _isGamesAwareForcedOn = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the current project has unsaved changes.
        /// This "dirty flag" is critical for prompting the user to save before closing or loading a new project.
        /// </summary>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty == value) return; // Avoid unnecessary updates if the state hasn't changed.
                _isDirty = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the UI for 6-digit security codes should be enabled.
        /// This property is controlled by the presence of 3/7-digit file paths.
        /// </summary>
        public bool IsSixDigitSecurityEnabled
        {
            get => _isSixDigitSecurityEnabled;
            set { _isSixDigitSecurityEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the UI for 3 and 7-digit security codes should be enabled.
        /// This property is controlled by the presence of a 6-digit file path.
        /// </summary>
        public bool IsThreeAndSevenDigitSecurityEnabled
        {
            get => _isThreeAndSevenDigitSecurityEnabled;
            set { _isThreeAndSevenDigitSecurityEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets a collection of validation issues found in the current project.
        /// </summary>
        public ObservableCollection<ValidationIssue> ValidationIssues { get; }

        /// <summary>
        /// Exposes the LosingTicketNearMissWeighting property for binding in the View.
        /// </summary>
        public NearMissWeighting LosingTicketNearMissWeighting
        {
            get => CurrentProject?.Settings.LosingTicketNearMissWeighting ?? NearMissWeighting.High;
            set
            {
                if (CurrentProject != null && CurrentProject.Settings.LosingTicketNearMissWeighting != value)
                {
                    CurrentProject.Settings.LosingTicketNearMissWeighting = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Commands

        // Defines all ICommand properties that the View can bind to for user actions.
        // This decouples the View (e.g., a Button) from the action's implementation in the ViewModel.

        public RelayCommand AddSymbolCommand { get; }
        public RelayCommand RemoveSymbolCommand { get; }
        public RelayCommand AddNumericSymbolCommand { get; }
        public RelayCommand RemoveNumericSymbolCommand { get; }
        public RelayCommand AddPrizeTierCommand { get; }
        public RelayCommand RemovePrizeTierCommand { get; }
        public RelayCommand MoveModuleUpCommand { get; }
        public RelayCommand MoveModuleDownCommand { get; }
        public RelayCommand DuplicateModuleCommand { get; }
        public ICommand ExportPrizesCommand { get; }
        public ICommand ImportPrizesCommand { get; }
        public ICommand ExportSymbolsCommand { get; }
        public ICommand ImportSymbolsCommand { get; }
        public ICommand BrowseThreeDigitCommand { get; }
        public ICommand BrowseSevenDigitCommand { get; }
        public ICommand BrowseSixDigitCommand { get; }
        public ICommand BrowseForSymbolImageCommand { get; }
        public ICommand ClearThreeDigitCommand { get; }
        public ICommand ClearSevenDigitCommand { get; }
        public ICommand ClearSixDigitCommand { get; }

        #endregion

        #region Analysis Properties

        // These properties hold the calculated results of the live game analysis.
        // They are bound to the "Game Analysis" panel in the UI to provide real-time feedback.

        public long LiveTotalTickets { get; private set; }
        public long LiveLvwWinners { get; private set; }
        public long LiveHvwWinners { get; private set; }
        public long LiveTotalWinners { get; private set; }
        public decimal LiveTotalSales { get; private set; }
        public decimal LiveTotalPrizeFund { get; private set; }
        public string LiveOdds { get; private set; }
        public decimal LivePayoutPercentage { get; private set; }
        public long PrintTotalTickets { get; private set; }
        public long PrintLvwWinners { get; private set; }
        public long PrintHvwWinners { get; private set; }
        public long PrintTotalWinners { get; private set; }
        public decimal PrintTotalSales { get; private set; }
        public decimal PrintTotalPrizeFund { get; private set; }
        public string PrintOdds { get; private set; }
        public decimal PrintPayoutPercentage { get; private set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="ProjectEditorViewModel"/> class.
        /// </summary>
        /// /// <param name="undoRedoService">The single, application-wide instance of the Undo/Redo service.</param>
        public ProjectEditorViewModel(UndoRedoService undoRedoService)
        {
            // Store the reference to the shared service.
            _undoRedoService = undoRedoService;

            // Populate the list of game modules available in the UI palette.
            // These are transient objects used only for display and initiating drag-and-drop.
            AvailableGameModules = new ObservableCollection<GameModule>
            {
                new MatchSymbolsInGridGame { ModuleName = "Match Symbols in Grid" },
                new MatchPrizesInGridGame { ModuleName = "Match Prizes in Grid" },
                new FindWinningSymbolGame { ModuleName = "Find Winning Symbol" },
                new MatchSymbolsInRowGame { ModuleName = "Match Symbols in Row" },
                new MatchPrizesInRowGame { ModuleName = "Match Prizes in Row" },
                new ChristmasTreeGame { ModuleName = "Christmas Tree" },
                new OnlineBonusGame { ModuleName = "Online Bonus (QR)", Url = "www.bonus-games.co.uk/" },
                new MatchSymbolToPrizeGame { ModuleName = "Match Symbol to Prize" }
            };

            // Initialise all commands used by the editor UI, linking them to their handler methods.
            AddSymbolCommand = new RelayCommand(AddSymbol, p => CurrentProject != null);
            RemoveSymbolCommand = new RelayCommand(RemoveSymbol, p => p is Symbol);
            AddNumericSymbolCommand = new RelayCommand(AddNumericSymbol, p => CurrentProject != null);
            RemoveNumericSymbolCommand = new RelayCommand(RemoveNumericSymbol, p => p is Symbol);
            AddPrizeTierCommand = new RelayCommand(AddPrizeTier, p => CurrentProject != null);
            RemovePrizeTierCommand = new RelayCommand(RemovePrizeTier, p => p is PrizeTier prize && prize.Value > 0);
            MoveModuleUpCommand = new RelayCommand(MoveModuleUp, CanMoveModuleUp);
            MoveModuleDownCommand = new RelayCommand(MoveModuleDown, CanMoveModuleDown);
            DuplicateModuleCommand = new RelayCommand(DuplicateModule, p => p is GameModule);
            ExportPrizesCommand = new RelayCommand(ExportPrizes, p => CurrentProject != null);
            ImportPrizesCommand = new RelayCommand(ImportPrizes, p => CurrentProject != null);
            ExportSymbolsCommand = new RelayCommand(ExportSymbols, p => CurrentProject != null);
            ImportSymbolsCommand = new RelayCommand(ImportSymbols, p => CurrentProject != null);
            BrowseThreeDigitCommand = new RelayCommand(BrowseForThreeDigitFile, p => CurrentProject != null);
            BrowseSevenDigitCommand = new RelayCommand(BrowseForSevenDigitFile, p => CurrentProject != null);
            BrowseSixDigitCommand = new RelayCommand(BrowseForSixDigitFile, p => CurrentProject != null);
            BrowseForSymbolImageCommand = new RelayCommand(BrowseForSymbolImage, p => p is Symbol);
            ClearThreeDigitCommand = new RelayCommand(p => CurrentProject.Security.ThreeDigitCodeFilePath = null, p => CurrentProject != null);
            ClearSevenDigitCommand = new RelayCommand(p => CurrentProject.Security.SevenDigitCodeFilePath = null, p => CurrentProject != null);
            ClearSixDigitCommand = new RelayCommand(p => CurrentProject.Security.SixDigitCodeFilePath = null, p => CurrentProject != null);

            // Initialise the validation service, issues collection, and the debounce timer.
            _validationService = new ProjectValidationService();
            ValidationIssues = new ObservableCollection<ValidationIssue>();
            _validationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Validate 500ms after the last change.
            };
            _validationTimer.Tick += (s, e) => RunValidation();
            FilteredValidationIssues = CollectionViewSource.GetDefaultView(ValidationIssues);
            FilteredValidationIssues.Filter = FilterValidationIssues;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Central handler for any change that should mark the project as dirty and trigger validation.
        /// </summary>
        private void OnProjectChanged(object sender = null, EventArgs e = null)
        {

            IsDirty = true;
            // Instead of running validation instantly, we reset a timer.
            // This prevents the validation from running on every single keystroke,
            // waiting until the user has paused typing.
            _validationTimer.Stop();
            _validationTimer.Start();
        }

        /// <summary>
        /// An event handler that triggers a recalculation of the game analysis when a relevant property changes.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event arguments, containing the name of the changed property.</param>
        private void RecalculateAnalysis_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // We only need to know that something changed, not what. The recalculation will use all current values.
            RecalculateAnalysis();
        }

        /// <summary>
        /// Attaches all necessary event handlers to a new project and its collections.
        /// This is crucial for detecting changes and updating the UI and analysis panels in real-time.
        /// </summary>
        /// <param name="project">The project to attach event handlers to.</param>
        private void AttachProjectEventHandlers(ScratchCardProject project)
        {
            // Subscribe to PropertyChanged events on settings and security objects.
            project.Settings.PropertyChanged += OnProjectChanged;
            project.Settings.PropertyChanged += RecalculateAnalysis_OnPropertyChanged;
            project.Security.PropertyChanged += SecuritySettings_PropertyChanged;

            // Subscribe to CollectionChanged events for all key collections.
            project.PrizeTiers.CollectionChanged += OnPrizeOrSymbolCollectionChanged;
            project.AvailableSymbols.CollectionChanged += OnPrizeOrSymbolCollectionChanged;
            project.NumericSymbols.CollectionChanged += OnPrizeOrSymbolCollectionChanged;
            project.Layout.GameModules.CollectionChanged += OnGameModulesChanged;

            // Subscribe to PropertyChanged events for each individual item within the collections.
            foreach (var prize in project.PrizeTiers)
            {
                prize.PropertyChanged += OnProjectChanged;
                prize.PropertyChanged += RecalculateAnalysis_OnPropertyChanged;
            }
            foreach (var symbol in project.AvailableSymbols) symbol.PropertyChanged += OnProjectChanged;
            foreach (var symbol in project.NumericSymbols) symbol.PropertyChanged += OnProjectChanged;
            foreach (var module in project.Layout.GameModules) module.PropertyChanged += OnProjectChanged;
        }

        /// <summary>
        /// Detaches all event handlers from a project.
        /// This must be called when a project is closed or replaced to prevent memory leaks by ensuring
        /// the garbage collector can reclaim the old project object.
        /// </summary>
        /// <param name="project">The project to detach event handlers from.</param>
        private void DetachProjectEventHandlers(ScratchCardProject project)
        {
            project.Settings.PropertyChanged -= OnProjectChanged;
            project.Settings.PropertyChanged -= RecalculateAnalysis_OnPropertyChanged;
            project.Security.PropertyChanged -= SecuritySettings_PropertyChanged;
            project.PrizeTiers.CollectionChanged -= OnPrizeOrSymbolCollectionChanged;
            project.AvailableSymbols.CollectionChanged -= OnPrizeOrSymbolCollectionChanged;
            project.NumericSymbols.CollectionChanged -= OnPrizeOrSymbolCollectionChanged;
            project.Layout.GameModules.CollectionChanged -= OnGameModulesChanged;

            // Unsubscribe from each item within the collections.
            foreach (var prize in project.PrizeTiers)
            {
                prize.PropertyChanged -= OnProjectChanged;
                prize.PropertyChanged -= RecalculateAnalysis_OnPropertyChanged;
            }
            foreach (var symbol in project.AvailableSymbols) symbol.PropertyChanged -= OnProjectChanged;
            foreach (var symbol in project.NumericSymbols) symbol.PropertyChanged -= OnProjectChanged;
            foreach (var module in project.Layout.GameModules) module.PropertyChanged -= OnProjectChanged;
        }

        /// <summary>
        /// Handles property changes on the SecuritySettings object to update the UI state.
        /// </summary>
        /// <param name="sender">The SecuritySettings object.</param>
        /// <param name="e">The property change event arguments.</param>
        private void SecuritySettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // We only care about changes to the file path properties.
            if (e.PropertyName == nameof(SecuritySettings.SixDigitCodeFilePath) ||
                e.PropertyName == nameof(SecuritySettings.ThreeDigitCodeFilePath) ||
                e.PropertyName == nameof(SecuritySettings.SevenDigitCodeFilePath))
            {
                UpdateSecurityControlStates();
            }
            OnProjectChanged(sender, e);
        }

        /// <summary>
        /// Handles the CollectionChanged event for prize and symbol collections.
        /// It attaches/detaches PropertyChanged handlers to items as they are added or removed.
        /// </summary>
        /// <param name="sender">The collection that changed.</param>
        /// <param name="e">The event arguments, detailing the changes to the collection.</param>
        private void OnPrizeOrSymbolCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // If prizes are added/removed, we need to attach/detach the recalculation logic to the items.
            if (sender == CurrentProject.PrizeTiers)
            {
                if (e.NewItems != null) foreach (INotifyPropertyChanged item in e.NewItems) item.PropertyChanged += RecalculateAnalysis_OnPropertyChanged;
                if (e.OldItems != null) foreach (INotifyPropertyChanged item in e.OldItems) item.PropertyChanged -= RecalculateAnalysis_OnPropertyChanged;
            }

            // For any collection change, we need to attach/detach the IsDirty logic.
            if (e.NewItems != null) foreach (INotifyPropertyChanged item in e.NewItems) item.PropertyChanged += OnProjectChanged;
            if (e.OldItems != null) foreach (INotifyPropertyChanged item in e.OldItems) item.PropertyChanged -= OnProjectChanged;

            OnProjectChanged();
        }

        /// <summary>
        /// Handles the CollectionChanged event for the GameModules collection.
        /// </summary>
        /// <param name="sender">The GameModules collection.</param>
        /// <param name="e">The event arguments detailing the changes.</param>
        private void OnGameModulesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null) foreach (GameModule item in e.NewItems) item.PropertyChanged += OnProjectChanged;
            if (e.OldItems != null) foreach (GameModule item in e.OldItems) item.PropertyChanged -= OnProjectChanged;

            // When modules are added or removed, we need to update related states.
            UpdateGamesAwareStatus();
            RenumberGameModules();
            OnProjectChanged();
        }

        #endregion

        #region Validation Logic

        /// <summary>
        /// The predicate method used by the ICollectionView to filter issues.
        /// </summary>
        /// <param name="item">The ValidationIssue object to be checked.</param>
        /// <returns>True if the item should be displayed; otherwise, false.</returns>
        private bool FilterValidationIssues(object item)
        {
            if (item is not ValidationIssue issue) return false;

            // Rule 1: Always show critical errors.
            if (issue.Severity == IssueSeverity.Error)
            {
                return true;
            }

            // Rule 2: Only show warnings if the user has the checkbox ticked.
            if (issue.Severity == IssueSeverity.Warning && ShowWarnings)
            {
                return true;
            }

            // Otherwise, hide the issue.
            return false;
        }

        /// <summary>
        /// Runs the validation service against the current project and updates the issues collection.
        /// </summary>
        public void RunValidation()
        {
            _validationTimer.Stop(); // Stop the timer to prevent it re-triggering.
            if (CurrentProject == null) return;

            // Get the list of issues from our dedicated service.
            var issues = _validationService.Validate(CurrentProject);

            // Update the observable collection, which will automatically update the UI.
            ValidationIssues.Clear();
            foreach (var issue in issues)
            {
                ValidationIssues.Add(issue);
            }
        }

        #endregion

        #region Command Logic

        /// <summary>
        /// Command handler for adding a new default symbol to the main symbol list.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void AddSymbol(object parameter)
        {
            // Find the next available ID by taking the max existing ID and adding 1.
            int nextId = CurrentProject.AvailableSymbols.Any() ? CurrentProject.AvailableSymbols.Max(s => s.Id) + 1 : 1;
            var newSymbol = new Symbol { Id = nextId, DisplayText = "New Symbol" };

            // Create a command object that knows how to add (and undo by removing) the new symbol.
            var command = new AddItemCommand<Symbol>(CurrentProject.AvailableSymbols, newSymbol);

            // Pass the command to the service to be executed and stored in the undo history.
            _undoRedoService.ExecuteCommand(command);
        }

        /// <summary>
        /// Command handler for removing the selected symbol from the main symbol list.
        /// </summary>
        /// <param name="parameter">The Symbol object to remove, passed from the view.</param>
        private void RemoveSymbol(object parameter)
        {
            if (parameter is Symbol symbolToRemove)
            {
                var gamesUsingSymbol = CurrentProject.Layout.GameModules
                    .OfType<FindSymbolGameBase>()
                    .Where(g => g.WinningSymbolId == symbolToRemove.Id)
                    .ToList();

                if (gamesUsingSymbol.Any())
                {
                    var gameNames = string.Join(", ", gamesUsingSymbol.Select(g => $"'{g.ModuleName}' (Game {g.GameNumber})"));
                    var result = MessageBox.Show(
                        $"This symbol is currently in use by the following game(s):\n\n{gameNames}\n\nRemoving it will reset their 'Winning Symbol' property. Are you sure you want to continue?",
                        "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No) return;

                    foreach (var game in gamesUsingSymbol)
                    {
                        game.WinningSymbolId = 0;
                    }
                }
                var command = new RemoveItemCommand<Symbol>(CurrentProject.AvailableSymbols, symbolToRemove);
                _undoRedoService.ExecuteCommand(command);
            }
        }

        /// <summary>
        /// Command handler for adding a new default symbol to the numeric symbol list.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void AddNumericSymbol(object parameter)
        {
            int nextId = CurrentProject.NumericSymbols.Any() ? CurrentProject.NumericSymbols.Max(s => s.Id) + 1 : 1;
            var newSymbol = new Symbol { Id = nextId, DisplayText = "New Number" };
            var command = new AddItemCommand<Symbol>(CurrentProject.NumericSymbols, newSymbol);
            _undoRedoService.ExecuteCommand(command);
        }

        /// <summary>
        /// Command handler for removing the selected symbol from the numeric symbol list.
        /// </summary>
        /// <param name="parameter">The Symbol object to remove.</param>
        private void RemoveNumericSymbol(object parameter)
        {
            if (parameter is Symbol symbolToRemove)
            {
                var gamesUsingSymbol = CurrentProject.Layout.GameModules
                    .OfType<MatchSymbolToPrizeGame>()
                    .Where(g => g.WinningSymbolId == symbolToRemove.Id)
                    .ToList();

                if (gamesUsingSymbol.Any())
                {
                    var gameNames = string.Join(", ", gamesUsingSymbol.Select(g => $"'{g.ModuleName}' (Game {g.GameNumber})"));
                    var result = MessageBox.Show(
                        $"This game symbol is currently in use by:\n\n{gameNames}\n\nRemoving it will break the link between this game and its prize. Are you sure you want to continue?",
                        "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No) return;

                    foreach (var game in gamesUsingSymbol)
                    {
                        game.WinningSymbolId = 0;
                    }
                }
                var command = new RemoveItemCommand<Symbol>(CurrentProject.NumericSymbols, symbolToRemove);
                _undoRedoService.ExecuteCommand(command);
            }
        }

        /// <summary>
        /// Command handler for adding a new default prize tier to the prize list.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void AddPrizeTier(object parameter)
        {
            int nextId = CurrentProject.PrizeTiers.Any() ? CurrentProject.PrizeTiers.Max(p => p.Id) + 1 : 1;
            var newPrize = new PrizeTier { Id = nextId, Value = 1, DisplayText = "New Prize", TextCode = "NEW" };
            var command = new AddItemCommand<PrizeTier>(CurrentProject.PrizeTiers, newPrize);
            _undoRedoService.ExecuteCommand(command);
            RecalculateAnalysis();
        }

        /// <summary>
        /// Command handler for removing the selected prize tier.
        /// </summary>
        /// <param name="parameter">The PrizeTier object to remove.</param>
        private void RemovePrizeTier(object parameter)
        {
            // Ensure we only allow removal of prize tiers with a value greater and not the NO WIN tier 0.
            if (parameter is PrizeTier prizeToRemove && prizeToRemove.Value > 0)
            {
                var gamesUsingPrize = CurrentProject.Layout.GameModules
                    .OfType<MatchSymbolToPrizeGame>()
                    .Where(g => CurrentProject.NumericSymbols.FirstOrDefault(s => s.Id == g.WinningSymbolId)?.Name == prizeToRemove.TextCode)
                    .ToList();

                if (gamesUsingPrize.Any())
                {
                    var gameNames = string.Join(", ", gamesUsingPrize.Select(g => $"'{g.ModuleName}' (Game {g.GameNumber})"));
                    var result = MessageBox.Show(
                        $"This prize tier is directly linked to a symbol used by:\n\n{gameNames}\n\nRemoving it will break the link and may cause validation errors. Are you sure you want to continue?",
                        "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No) return;
                }
                var command = new RemoveItemCommand<PrizeTier>(CurrentProject.PrizeTiers, prizeToRemove);
                _undoRedoService.ExecuteCommand(command);
                RecalculateAnalysis();
            }
        }

        /// <summary>
        /// Predicate for the MoveModuleUpCommand, determining if the selected module can be moved.
        /// </summary>
        /// <param name="parameter">The selected GameModule.</param>
        /// <returns>True if the module can be moved up; otherwise, false.</returns>
        private bool CanMoveModuleUp(object parameter)
        {
            if (parameter is GameModule module && CurrentProject?.Layout.GameModules != null)
            {
                // Can only move up if it's not already the first item.
                return CurrentProject.Layout.GameModules.IndexOf(module) > 0;
            }
            return false;
        }

        /// <summary>
        /// Command handler for moving the selected game module up in the order, affecting its Game Number.
        /// </summary>
        /// <param name="parameter">The GameModule to move.</param>
        private void MoveModuleUp(object parameter)
        {
            if (parameter is GameModule module)
            {
                int index = CurrentProject.Layout.GameModules.IndexOf(module);
                if (index > 0)
                {
                    var command = new MoveItemCommand<GameModule>(CurrentProject.Layout.GameModules, module, -1);
                    _undoRedoService.ExecuteCommand(command);
                    RenumberGameModules(); // Re-assign all game numbers after moving.
                }
            }
        }

        /// <summary>
        /// Predicate for the MoveModuleDownCommand.
        /// </summary>
        /// <param name="parameter">The selected GameModule.</param>
        /// <returns>True if the module can be moved down; otherwise, false.</returns>
        private bool CanMoveModuleDown(object parameter)
        {
            if (parameter is GameModule module && CurrentProject?.Layout.GameModules != null)
            {
                int index = CurrentProject.Layout.GameModules.IndexOf(module);
                // Can only move down if it's not already the last item.
                return index >= 0 && index < CurrentProject.Layout.GameModules.Count - 1;
            }
            return false;
        }

        /// <summary>
        /// Command handler for moving the selected game module down in the order.
        /// </summary>
        /// <param name="parameter">The GameModule to move.</param>
        private void MoveModuleDown(object parameter)
        {
            if (parameter is GameModule module)
            {
                int index = CurrentProject.Layout.GameModules.IndexOf(module);
                if (index >= 0 && index < CurrentProject.Layout.GameModules.Count - 1)
                {
                    var command = new MoveItemCommand<GameModule>(CurrentProject.Layout.GameModules, module, 1);
                    _undoRedoService.ExecuteCommand(command);
                    RenumberGameModules();
                }
            }
        }

        /// <summary>
        /// Command handler for creating a deep clone of the selected game module and adding it to the designer.
        /// </summary>
        /// <param name="parameter">The GameModule to duplicate.</param>
        private void DuplicateModule(object parameter)
        {
            if (parameter is not GameModule originalModule) return;

            var newModule = DeepCloneModule(originalModule);
            if (newModule == null) return;

            // Offset the new module so it doesn't appear directly on top of the original.
            newModule.Position = new Point(originalModule.Position.X + 20, originalModule.Position.Y + 20);

            var command = new AddItemCommand<GameModule>(CurrentProject.Layout.GameModules, newModule);
            _undoRedoService.ExecuteCommand(command);

            SelectedModule = newModule; // Automatically select the newly created module.
        }

        #endregion

        #region NEW: Undoable Action Handlers

        /// <summary>
        /// Creates an undoable command for a property change that originates from the UI.
        /// This is called by code-behind event handlers (e.g., for DataGrid cell edits).
        /// </summary>
        /// <param name="target">The data object being changed.</param>
        /// <param name="propertyName">The name of the property that changed.</param>
        /// <param name="oldValue">The value before the change.</param>
        /// <param name="newValue">The new value after the change.</param>
        public void CreatePropertyChangeCommand(object target, string propertyName, object oldValue, object newValue)
        {
            // Do nothing if the value hasn't actually changed.
            if (Equals(oldValue, newValue)) return;

            var command = new UpdatePropertyCommand(target, propertyName, oldValue, newValue);
            _undoRedoService.ExecuteCommand(command);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Ensures that a "NO WIN" prize tier (value 0) always exists in the project.
        /// This is required by the generation logic to fill non-winning slots in packs.
        /// </summary>
        private void EnsureLoserTierExists()
        {
            var loserTier = CurrentProject.PrizeTiers.FirstOrDefault(p => p.Value == 0);
            if (loserTier == null)
            {
                loserTier = new PrizeTier { Id = 0, Value = 0 };
                CurrentProject.PrizeTiers.Add(loserTier);
            }
            // Ensure its properties are always set correctly, including matching the job's loser barcode.
            loserTier.DisplayText = "NO WIN";
            loserTier.TextCode = "NOWIN";
            loserTier.Barcode = CurrentProject.Settings.LoserBarcode;
        }

        /// <summary>
        /// Creates a deep clone of a GameModule object using JSON serialisation.
        /// This is an effective way to create a completely new, independent instance of an object without manually copying every property.
        /// </summary>
        /// <param name="originalModule">The module to clone.</param>
        /// <returns>A new, independent instance of the game module.</returns>
        public GameModule DeepCloneModule(GameModule originalModule)
        {
            try
            {
                var moduleType = originalModule.GetType();
                string json = JsonSerializer.Serialize(originalModule, moduleType);
                return (GameModule)JsonSerializer.Deserialize(json, moduleType);
            }
            catch (Exception ex)
            {
                // If cloning fails, inform the user. This shouldn't happen in normal operation.
                MessageBox.Show($"Could not duplicate the module.\n\nError: {ex.Message}", "Duplicate Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// Iterates through the game modules and re-assigns their GameNumber property based on their order in the collection.
        /// This ensures game numbers are always sequential (1, 2, 3...).
        /// </summary>
        private void RenumberGameModules()
        {
            if (CurrentProject?.Layout?.GameModules == null) return;
            for (int i = 0; i < CurrentProject.Layout.GameModules.Count; i++)
            {
                CurrentProject.Layout.GameModules[i].GameNumber = i + 1;
            }
        }

        /// <summary>
        /// Checks the project configuration to determine if the "Games Aware" feature should be forced on.
        /// This is a safety feature to prevent conflicts between multiple "Match Symbol to Prize" games.
        /// </summary>
        private void UpdateGamesAwareStatus()
        {
            if (CurrentProject == null) return;
            // If there are 2 or more MatchSymbolToPrize games, "Games Aware" is mandatory to prevent
            // one game from accidentally creating a win in another.
            bool forceOn = CurrentProject.Layout.GameModules.OfType<MatchSymbolToPrizeGame>().Count() >= 2;
            IsGamesAwareForcedOn = forceOn;
            if (forceOn) CurrentProject.Settings.GamesAware = true;
        }

        /// <summary>
        /// Toggles the enabled state of the security file input controls based on which method is being used.
        /// The 6-digit method and the 3+7-digit method are mutually exclusive.
        /// </summary>
        private void UpdateSecurityControlStates()
        {
            if (CurrentProject == null) return;

            // If a path for the 6-digit file exists, disable the 3/7-digit controls.
            bool hasSixDigitPath = !string.IsNullOrWhiteSpace(CurrentProject.Security.SixDigitCodeFilePath);
            IsThreeAndSevenDigitSecurityEnabled = !hasSixDigitPath;

            // If a path for either the 3 or 7-digit file exists, disable the 6-digit control.
            bool hasThreeOrSevenDigitPath = !string.IsNullOrWhiteSpace(CurrentProject.Security.ThreeDigitCodeFilePath) ||
                                            !string.IsNullOrWhiteSpace(CurrentProject.Security.SevenDigitCodeFilePath);
            IsSixDigitSecurityEnabled = !hasThreeOrSevenDigitPath;
        }

        /// <summary>
        /// Calculates all the live analysis statistics based on the current project settings and prize structure.
        /// This method is called frequently to provide real-time feedback to the user in the analysis panel.
        /// </summary>
        public void RecalculateAnalysis()
        {
            // Prevent re-entrancy and handle null project.
            if (_isRecalculating || CurrentProject == null || CurrentProject.PrizeTiers == null) return;

            try
            {
                _isRecalculating = true;

                // First, ensure the loser tier LVW count is correct. It's calculated to fill the remaining slots in a pack.
                var loserTier = CurrentProject.PrizeTiers.FirstOrDefault(p => p.Value == 0);
                if (loserTier != null)
                {
                    int totalLvwWinnersInPack = CurrentProject.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount);
                    loserTier.LvwWinnerCount = CurrentProject.Settings.CardsPerPack - totalLvwWinnersInPack;
                }

                // --- Live Run Calculations (based on packs intended for sale) ---
                LiveTotalTickets = (long)CurrentProject.Settings.TotalPacks * CurrentProject.Settings.CardsPerPack;
                LiveLvwWinners = (long)CurrentProject.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount) * CurrentProject.Settings.TotalPacks;
                LiveHvwWinners = CurrentProject.PrizeTiers.Sum(p => p.HvwWinnerCount);
                LiveTotalWinners = LiveLvwWinners + LiveHvwWinners;
                decimal liveLvwFund = (decimal)CurrentProject.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount * (decimal)p.Value) * CurrentProject.Settings.TotalPacks;
                decimal liveHvwFund = CurrentProject.PrizeTiers.Sum(p => p.HvwWinnerCount * (decimal)p.Value);
                LiveTotalPrizeFund = liveLvwFund + liveHvwFund;
                LiveTotalSales = LiveTotalTickets * CurrentProject.Settings.TicketSalePrice;
                LiveOdds = LiveTotalWinners > 0 ? $"1 in {(double)LiveTotalTickets / LiveTotalWinners:F2}" : "N/A";
                LivePayoutPercentage = LiveTotalSales > 0 ? (LiveTotalPrizeFund / LiveTotalSales) * 100 : 0;

                // --- Print Run Calculations (based on total printed packs, including extras) ---
                PrintTotalTickets = (long)CurrentProject.Settings.PrintPacks * CurrentProject.Settings.CardsPerPack;
                PrintLvwWinners = (long)CurrentProject.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount) * CurrentProject.Settings.PrintPacks;
                PrintHvwWinners = LiveHvwWinners; // HVW count is for the total job, not per pack.
                PrintTotalWinners = PrintLvwWinners + PrintHvwWinners;
                decimal printLvwFund = (decimal)CurrentProject.PrizeTiers.Where(p => p.Value > 0).Sum(p => p.LvwWinnerCount * p.Value) * CurrentProject.Settings.PrintPacks;
                PrintTotalPrizeFund = printLvwFund + liveHvwFund;
                PrintTotalSales = PrintTotalTickets * CurrentProject.Settings.TicketSalePrice;
                PrintOdds = PrintTotalWinners > 0 ? $"1 in {(double)PrintTotalTickets / PrintTotalWinners:F2}" : "N/A";
                PrintPayoutPercentage = PrintTotalSales > 0 ? (PrintTotalPrizeFund / PrintTotalSales) * 100 : 0;

                // Notify the UI that all analysis properties have been updated.
                OnPropertyChanged(nameof(LiveTotalTickets));
                OnPropertyChanged(nameof(LiveLvwWinners));
                OnPropertyChanged(nameof(LiveHvwWinners));
                OnPropertyChanged(nameof(LiveTotalWinners));
                OnPropertyChanged(nameof(LiveTotalSales));
                OnPropertyChanged(nameof(LiveTotalPrizeFund));
                OnPropertyChanged(nameof(LiveOdds));
                OnPropertyChanged(nameof(LivePayoutPercentage));
                OnPropertyChanged(nameof(PrintTotalTickets));
                OnPropertyChanged(nameof(PrintLvwWinners));
                OnPropertyChanged(nameof(PrintHvwWinners));
                OnPropertyChanged(nameof(PrintTotalWinners));
                OnPropertyChanged(nameof(PrintTotalSales));
                OnPropertyChanged(nameof(PrintTotalPrizeFund));
                OnPropertyChanged(nameof(PrintOdds));
                OnPropertyChanged(nameof(PrintPayoutPercentage));
            }
            finally
            {
                // Ensure the flag is always reset, even if an error occurs.
                _isRecalculating = false;
            }
        }
        #endregion

        #region Import/Export Logic

        /// <summary>
        /// A private class used as a Data Transfer Object (DTO) for exporting/importing both symbol lists in a single file.
        /// This simplifies the JSON structure for symbol management.
        /// </summary>
        private class SymbolExportData
        {
            public List<Symbol> AvailableSymbols { get; set; }
            public List<Symbol> NumericSymbols { get; set; }
        }

        /// <summary>
        /// Command handler for exporting prize tiers. Serialises the prize tiers to a JSON file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void ExportPrizes(object parameter)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Export Prize Tiers",
                Filter = "JSON Prize File (*.json)|*.json",
                FileName = "PrizeTiers.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(CurrentProject.PrizeTiers, options);
                    File.WriteAllText(sfd.FileName, jsonString);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not export prizes: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Command handler for importing prize tiers. Deserialises prize tiers from a JSON file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void ImportPrizes(object parameter)
        {
            if (MessageBox.Show("This will replace all current prize tiers. Continue?", "Confirm Import", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No) return;
            var ofd = new OpenFileDialog { Title = "Import Prize Tiers", Filter = "JSON Prize File (*.json)|*.json" };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string jsonString = File.ReadAllText(ofd.FileName);
                    var importedPrizes = JsonSerializer.Deserialize<ObservableCollection<PrizeTier>>(jsonString);
                    if (importedPrizes != null)
                    {
                        // To correctly update the UI, clear the existing collection and add new items,
                        // rather than replacing the collection instance itself.
                        CurrentProject.PrizeTiers.Clear();
                        foreach (var prize in importedPrizes)
                        {
                            CurrentProject.PrizeTiers.Add(prize);
                        }

                        RecalculateAnalysis();
                        EnsureLoserTierExists();
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Could not import prizes: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        /// <summary>
        /// Command handler for exporting symbols. Serialises both symbol lists to a single JSON file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void ExportSymbols(object parameter)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Export Symbol Lists",
                Filter = "JSON Symbol File (*.json)|*.json",
                FileName = "Symbols.json"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var exportData = new SymbolExportData
                    {
                        AvailableSymbols = new List<Symbol>(CurrentProject.AvailableSymbols),
                        NumericSymbols = new List<Symbol>(CurrentProject.NumericSymbols)
                    };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(exportData, options);
                    File.WriteAllText(sfd.FileName, jsonString);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not export symbols: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Command handler for importing symbols. Deserialises both symbol lists from a single JSON file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void ImportSymbols(object parameter)
        {
            if (MessageBox.Show("This will replace both symbol lists. Continue?", "Confirm Import", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No) return;
            var ofd = new OpenFileDialog { Title = "Import Symbol Lists", Filter = "JSON Symbol File (*.json)|*.json" };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string jsonString = File.ReadAllText(ofd.FileName);
                    var importedData = JsonSerializer.Deserialize<SymbolExportData>(jsonString);
                    if (importedData?.AvailableSymbols != null && importedData?.NumericSymbols != null)
                    {
                        // Clear and repopulate collections to maintain the original instance for data binding.
                        CurrentProject.AvailableSymbols.Clear();
                        foreach (var symbol in importedData.AvailableSymbols) CurrentProject.AvailableSymbols.Add(symbol);

                        CurrentProject.NumericSymbols.Clear();
                        foreach (var symbol in importedData.NumericSymbols) CurrentProject.NumericSymbols.Add(symbol);

                        OnProjectChanged();
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Could not import symbols: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        #endregion

        #region File Browser Logic

        /// <summary>
        /// Command handler that opens a file dialog for the user to select the 3-digit security code file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void BrowseForThreeDigitFile(object parameter)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select 3-Digit Code File",
                Filter = "RND Files (*.rnd)|*.rnd|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                // Create a command to handle the property change.
                var command = new UpdatePropertyCommand(CurrentProject.Security, nameof(SecuritySettings.ThreeDigitCodeFilePath), ofd.FileName);
                _undoRedoService.ExecuteCommand(command);
            }
        }

        /// <summary>
        /// Command handler that opens a file dialog for the user to select the 7-digit security code file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void BrowseForSevenDigitFile(object parameter)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select 7-Digit Code File",
                Filter = "RND Files (*.rnd)|*.rnd|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                var command = new UpdatePropertyCommand(CurrentProject.Security, nameof(SecuritySettings.SevenDigitCodeFilePath), ofd.FileName);
                _undoRedoService.ExecuteCommand(command);
            }
        }

        /// <summary>
        /// Command handler that opens a file dialog for the user to select the 6-digit security code file.
        /// </summary>
        /// <param name="parameter">The command parameter (not used).</param>
        private void BrowseForSixDigitFile(object parameter)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Select 6-Digit Code File",
                Filter = "RND Files (*.rnd)|*.rnd|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                var command = new UpdatePropertyCommand(CurrentProject.Security, nameof(SecuritySettings.SixDigitCodeFilePath), ofd.FileName);
                _undoRedoService.ExecuteCommand(command);
            }
        }

        /// <summary>
        /// Command handler that opens a file dialog for the user to select an image path for a symbol.
        /// </summary>
        /// <param name="parameter">The Symbol object whose ImagePath needs to be updated.</param>
        private void BrowseForSymbolImage(object parameter)
        {
            if (parameter is Symbol symbolToUpdate)
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Select Symbol Image",
                    Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                    // Pre-set the initial directory to a common location for convenience.
                    InitialDirectory = @"\\harlow.local\DFS\Gaming_Jobs\Rieves Symbols\Symbols 90%"
                };

                if (ofd.ShowDialog() == true)
                {
                    symbolToUpdate.ImagePath = ofd.FileName;
                }
            }
        }

        #endregion
    }
}