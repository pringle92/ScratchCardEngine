#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using LiveCharts;
using LiveCharts.Wpf;
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.ViewModels;
using ScratchCardGenerator.Common.ViewModels.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;

#endregion

namespace ScratchCardGenerator.ViewModels
{
    #region Data Transfer Objects

    /// <summary>
    /// A simple Data Transfer Object (DTO) class to hold the analysis results for a single game module,
    /// used for display in the top data grid of the analysis window.
    /// </summary>
    public class GameAnalysisDetail
    {
        public int GameNumber { get; set; }
        public string GameName { get; set; }
        public long TotalWins { get; set; }
        public string WinSharePercentage { get; set; }
    }

    /// <summary>
    /// A helper DTO class to store the fully calculated prize distribution data for a single prize in a single game.
    /// A collection of these objects forms the raw data source that is later filtered for the charts.
    /// </summary>
    public class PrizeDistributionData
    {
        public int GameNumber { get; set; }
        public PrizeTier Prize { get; set; }
        public long LvwCount { get; set; }
        public long HvwCount { get; set; }
    }

    #endregion

    /// <summary>
    /// The ViewModel for the Advanced Analysis window. It is responsible for performing detailed calculations
    /// on a project's prize structure, allocating wins to specific game modules, and preparing the data for display in charts.
    /// </summary>
    public class AdvancedAnalysisViewModel : ViewModelBase
    {
        #region Private Fields

        /// <summary>
        /// A private list that holds the complete, unfiltered prize distribution data for all games.
        /// This serves as the master data source that the chart-updating logic filters.
        /// </summary>
        private readonly List<PrizeDistributionData> _fullPrizeData;

        /// <summary>
        /// The backing field for the SelectedGame property.
        /// </summary>
        private GameAnalysisDetail _selectedGame;

        /// <summary>
        /// The backing field for the ChartTitle property.
        /// </summary>
        private string _chartTitle = "Prize Distribution (All Games)";

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the collection of per-game breakdown details for display in the main data grid.
        /// </summary>
        public ObservableCollection<GameAnalysisDetail> GameBreakdown { get; }

        /// <summary>
        /// Gets the data series for the Low-Value Winners chart, managed by the LiveCharts library.
        /// </summary>
        public SeriesCollection LvwPrizeSeries { get; private set; }

        /// <summary>
        /// Gets the data series for the High-Value Winners chart, managed by the LiveCharts library.
        /// </summary>
        public SeriesCollection HvwPrizeSeries { get; private set; }

        /// <summary>
        /// Gets the labels for the X-axis of the charts, corresponding to the prize tiers.
        /// </summary>
        public string[] PrizeTierLabels { get; private set; }

        /// <summary>
        /// Gets or sets the currently selected game from the breakdown list.
        /// The setter triggers an update of the prize distribution charts to show the data for the selected game.
        /// </summary>
        public GameAnalysisDetail SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame == value) return; // Avoid unnecessary updates.
                _selectedGame = value;
                OnPropertyChanged();
                UpdatePrizeDistributionCharts(); // Refresh chart data when the selection changes.
            }
        }

        /// <summary>
        /// Gets or sets the dynamic title for the prize distribution charts.
        /// </summary>
        public string ChartTitle
        {
            get => _chartTitle;
            set { _chartTitle = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets a formatter function for the Y-axis labels on the charts, to display numbers with thousands separators.
        /// </summary>
        public Func<double, string> YAxisFormatter { get; }

        #endregion

        #region Commands

        /// <summary>
        /// Gets the command to clear the current game selection, which resets the charts to show data for all games.
        /// </summary>
        public RelayCommand ClearSelectionCommand { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="AdvancedAnalysisViewModel"/> class.
        /// </summary>
        /// <param name="project">The ScratchCardProject to be analysed.</param>
        public AdvancedAnalysisViewModel(ScratchCardProject project)
        {
            // Initialise collections.
            GameBreakdown = new ObservableCollection<GameAnalysisDetail>();
            LvwPrizeSeries = new SeriesCollection();
            HvwPrizeSeries = new SeriesCollection();
            _fullPrizeData = new List<PrizeDistributionData>();

            // Initialise commands and formatters.
            ClearSelectionCommand = new RelayCommand(p => SelectedGame = null);
            YAxisFormatter = value => value.ToString("N0"); // Format as a number with commas (e.g., 1,000).

            // Perform all necessary calculations upon creation.
            CalculateGameBreakdown(project);
            CalculateAllPrizeData(project);
            UpdatePrizeDistributionCharts();
        }

        #endregion

        #region Private Calculation Methods

        /// <summary>
        /// Updates the chart series based on the currently selected game.
        /// If no game is selected, it shows the aggregated totals for all games.
        /// </summary>
        private void UpdatePrizeDistributionCharts()
        {
            // Dynamically set the chart title based on the current selection.
            ChartTitle = SelectedGame == null
                ? "Prize Distribution (All Games)"
                : $"Prize Distribution ({SelectedGame.GameName})";

            // Get a distinct, sorted list of all winnable prize tiers to serve as the chart's categories.
            var winnableTiers = _fullPrizeData.Select(d => d.Prize).Distinct().OrderBy(p => p.Value).ToList();
            PrizeTierLabels = winnableTiers.Select(p => p.IsOnlinePrize ? $"{p.DisplayText} (Online)" : p.DisplayText).ToArray();

            // Filter the master data source based on the user's selection.
            IEnumerable<PrizeDistributionData> dataToShow = _fullPrizeData;
            if (SelectedGame != null)
            {
                dataToShow = _fullPrizeData.Where(d => d.GameNumber == SelectedGame.GameNumber);
            }

            // Group the filtered data by prize and sum the LVW/HVW counts.
            var groupedData = dataToShow
                .GroupBy(d => d.Prize)
                .ToDictionary(g => g.Key, g => new { Lvw = g.Sum(i => i.LvwCount), Hvw = g.Sum(i => i.HvwCount) });

            // Create ChartValues collections, ensuring a value of 0 for any prize tier not present in the filtered data.
            var lvwValues = new ChartValues<long>(winnableTiers.Select(p => groupedData.ContainsKey(p) ? groupedData[p].Lvw : 0));
            var hvwValues = new ChartValues<long>(winnableTiers.Select(p => groupedData.ContainsKey(p) ? groupedData[p].Hvw : 0));

            // Clear and rebuild the LVW chart series.
            LvwPrizeSeries.Clear();
            LvwPrizeSeries.Add(new ColumnSeries
            {
                Title = "Low-Value Winners",
                Values = lvwValues,
                Fill = new SolidColorBrush(Color.FromRgb(99, 157, 255)) // Example colour
            });

            // Clear and rebuild the HVW chart series.
            HvwPrizeSeries.Clear();
            HvwPrizeSeries.Add(new ColumnSeries
            {
                Title = "High-Value Winners",
                Values = hvwValues,
                Fill = Brushes.Red // Example colour
            });

            // Notify the UI that the chart labels have been updated.
            OnPropertyChanged(nameof(PrizeTierLabels));
        }

        /// <summary>
        /// Calculates the per-game win allocation for the top data grid.
        /// This logic distributes the total number of wins for each prize tier among the eligible game modules.
        /// </summary>
        private void CalculateGameBreakdown(ScratchCardProject project)
        {
            if (!project.Layout.GameModules.Any()) return;

            // Initialise a dictionary to track win counts for each game number.
            var gameWinCounts = project.Layout.GameModules.ToDictionary(gm => gm.GameNumber, gm => 0L);
            long totalWinnableTickets = project.PrizeTiers.Where(p => p.Value > 0).Sum(p => (long)p.HvwWinnerCount + ((long)p.LvwWinnerCount * project.Settings.PrintPacks));

            // "Generic" modules are those that can award any standard prize (i.e., not special types like Online or Match-to-Prize).
            var genericWinnableModules = project.Layout.GameModules
                .Where(m => !(m is OnlineBonusGame) && !(m is MatchSymbolToPrizeGame))
                .ToList();

            // Iterate through each winnable prize tier to allocate its wins.
            foreach (var prize in project.PrizeTiers.Where(p => p.Value > 0))
            {
                long prizeWins = (long)prize.HvwWinnerCount + ((long)prize.LvwWinnerCount * project.Settings.PrintPacks);
                if (prizeWins == 0) continue;

                GameModule specificModule = null;
                // Rule: Online prizes can only be won by an OnlineBonusGame.
                if (prize.IsOnlinePrize)
                {
                    specificModule = project.Layout.GameModules.OfType<OnlineBonusGame>().FirstOrDefault();
                }
                // Rule: Prizes can be specifically linked to a MatchSymbolToPrizeGame via their TextCode.
                else
                {
                    specificModule = project.Layout.GameModules
                        .OfType<MatchSymbolToPrizeGame>()
                        .FirstOrDefault(g => project.NumericSymbols.FirstOrDefault(s => s.Id == g.WinningSymbolId)?.Name == prize.TextCode);
                }

                if (specificModule != null)
                {
                    // If a specific module must award this prize, allocate all wins to it.
                    if (gameWinCounts.ContainsKey(specificModule.GameNumber))
                    {
                        gameWinCounts[specificModule.GameNumber] += prizeWins;
                    }
                }
                else if (genericWinnableModules.Any())
                {
                    // If no specific module is required, distribute the wins evenly among all generic modules.
                    // This logic includes remainder distribution to ensure no "lost" wins due to integer division.
                    long winsPerGenericGame = prizeWins / genericWinnableModules.Count;
                    long remainder = prizeWins % genericWinnableModules.Count;

                    for (int i = 0; i < genericWinnableModules.Count; i++)
                    {
                        var module = genericWinnableModules[i];
                        if (gameWinCounts.ContainsKey(module.GameNumber))
                        {
                            long winsToAllocate = winsPerGenericGame;
                            if (i < remainder) // Distribute the remainder one by one to the first few games.
                            {
                                winsToAllocate++;
                            }
                            gameWinCounts[module.GameNumber] += winsToAllocate;
                        }
                    }
                }
            }

            // Populate the final collection for the data grid.
            foreach (var entry in gameWinCounts.OrderBy(e => e.Key))
            {
                double percentage = totalWinnableTickets > 0 ? ((double)entry.Value / totalWinnableTickets) * 100 : 0;
                GameBreakdown.Add(new GameAnalysisDetail
                {
                    GameNumber = entry.Key,
                    GameName = project.Layout.GameModules.First(m => m.GameNumber == entry.Key).ModuleName,
                    TotalWins = entry.Value,
                    WinSharePercentage = $"{percentage:F2}%"
                });
            }
        }

        /// <summary>
        /// Performs a detailed calculation of all prize distributions for every game, storing the raw data
        /// in the _fullPrizeData list for later filtering by the chart logic.
        /// </summary>
        private void CalculateAllPrizeData(ScratchCardProject project)
        {
            var genericWinnableModules = project.Layout.GameModules
                .Where(m => !(m is OnlineBonusGame) && !(m is MatchSymbolToPrizeGame))
                .ToList();

            foreach (var prize in project.PrizeTiers.Where(p => p.Value > 0))
            {
                long totalLvw = (long)prize.LvwWinnerCount * project.Settings.PrintPacks;
                long totalHvw = (long)prize.HvwWinnerCount;

                if (totalLvw == 0 && totalHvw == 0) continue;

                // This logic mirrors the allocation in CalculateGameBreakdown but stores the detailed data.
                GameModule specificModule = null;
                if (prize.IsOnlinePrize)
                {
                    specificModule = project.Layout.GameModules.OfType<OnlineBonusGame>().FirstOrDefault();
                }
                else
                {
                    specificModule = project.Layout.GameModules
                        .OfType<MatchSymbolToPrizeGame>()
                        .FirstOrDefault(g => project.NumericSymbols.FirstOrDefault(s => s.Id == g.WinningSymbolId)?.Name == prize.TextCode);
                }

                if (specificModule != null)
                {
                    _fullPrizeData.Add(new PrizeDistributionData
                    {
                        GameNumber = specificModule.GameNumber,
                        Prize = prize,
                        LvwCount = totalLvw,
                        HvwCount = totalHvw
                    });
                }
                else if (genericWinnableModules.Any())
                {
                    // Distribute both LVW and HVW counts evenly, including remainders.
                    long lvwPerGame = totalLvw / genericWinnableModules.Count;
                    long lvwRemainder = totalLvw % genericWinnableModules.Count;

                    long hvwPerGame = totalHvw / genericWinnableModules.Count;
                    long hvwRemainder = totalHvw % genericWinnableModules.Count;

                    for (int i = 0; i < genericWinnableModules.Count; i++)
                    {
                        var module = genericWinnableModules[i];
                        long lvwToAllocate = lvwPerGame + (i < lvwRemainder ? 1 : 0);
                        long hvwToAllocate = hvwPerGame + (i < hvwRemainder ? 1 : 0);

                        _fullPrizeData.Add(new PrizeDistributionData
                        {
                            GameNumber = module.GameNumber,
                            Prize = prize,
                            LvwCount = lvwToAllocate,
                            HvwCount = hvwToAllocate
                        });
                    }
                }
            }
        }

        #endregion
    }
}