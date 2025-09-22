#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using FinalGameChecker.ViewModels;
using KellermanSoftware.CompareNetObjects;
using ScratchCardGenerator.Common.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

#endregion

namespace FinalGameChecker.Services
{
    /// <summary>
    /// Provides services for performing a deep, property-by-property comparison of two ScratchCardProject objects
    /// and for launching external visual diff tools to display the differences.
    /// </summary>
    public class ProjectComparisonService
    {
        #region Public Methods

        /// <summary>
        /// Performs a deep comparison of two project objects using the CompareNetObjects library.
        /// </summary>
        /// <param name="originalProject">The project loaded from the generator's output.</param>
        /// <param name="checkerProject">The project manually recreated in the checker.</param>
        /// <returns>A tuple containing a boolean indicating if the objects are equal, and a list of detailed difference objects if they are not.</returns>
        public (bool AreEqual, List<DifferenceViewModel> Differences) CompareProjects(ScratchCardProject originalProject, ScratchCardProject checkerProject)
        {
            var differences = new List<DifferenceViewModel>();

            // Configure the comparison logic to tailor it to our specific needs.
            var config = new ComparisonConfig
            {
                // We explicitly ignore properties that are irrelevant to the game's generated data integrity.
                // - FilePath, Position, IsSelected: These are runtime/UI-specific and not part of the core data.
                // - Item: A property used by WPF's data binding.
                // - JobName, JobCode, Client: These are for identification and are allowed to differ without failing the check.
                MembersToIgnore = new List<string>
                {
                    "FilePath", "Item", "Position", "IsSelected",
                    "JobName", "JobCode", "Client"
                },

                // Set MaxDifferences to a high number to ensure we find all differences, not just the first one.
                MaxDifferences = 999,

                // This is a crucial part of the configuration. It tells the comparer how to match items within collections.
                // Instead of comparing by index (e.g., PrizeTiers[0] vs. PrizeTiers[0]), it will match items based on their unique IDs.
                // This makes the comparison robust against changes in item order.
                CollectionMatchingSpec = new Dictionary<Type, IEnumerable<string>>
                {
                    { typeof(PrizeTier), new[] { nameof(PrizeTier.Id) } },
                    { typeof(Symbol), new[] { nameof(Symbol.Id) } },
                    { typeof(GameModule), new[] { nameof(GameModule.GameNumber) } }
                }
            };

            var compareLogic = new CompareLogic(config);
            var result = compareLogic.Compare(originalProject, checkerProject);

            if (!result.AreEqual)
            {
                // If differences are found, transform them into our user-friendly DifferenceViewModel.
                foreach (var diff in result.Differences)
                {
                    differences.Add(new DifferenceViewModel(diff.PropertyName, diff.Object1Value, diff.Object2Value));
                }
            }

            return (result.AreEqual, differences);
        }

        /// <summary>
        /// Launches an external diff tool to show a side-by-side visual comparison of the two projects.
        /// It serialises both projects to temporary JSON files and then attempts to open them with common diff tools.
        /// </summary>
        /// <param name="originalProject">The original project object.</param>
        /// <param name="checkerProject">The checker's project object.</param>
        public void LaunchVisualDiff(ScratchCardProject originalProject, ScratchCardProject checkerProject)
        {
            string originalPath = "";
            string checkerPath = "";

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };

                // Use the ScratchCardProjectCopy helper class to create clean copies for serialization.
                // This prevents any runtime-only properties from being included in the JSON files.
                string originalJson = JsonSerializer.Serialize(new ScratchCardProjectCopy(originalProject), options);
                string checkerJson = JsonSerializer.Serialize(new ScratchCardProjectCopy(checkerProject), options);

                // Create temporary files to hold the JSON representations of the projects.
                originalPath = Path.Combine(Path.GetTempPath(), "original_project.json");
                checkerPath = Path.Combine(Path.GetTempPath(), "checker_project.json");

                File.WriteAllText(originalPath, originalJson);
                File.WriteAllText(checkerPath, checkerJson);

                // Attempt to launch common diff tools in a preferred order.
                string winMergePath = @"C:\Program Files\WinMerge\WinMergeU.exe";
                string vsCodePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe");

                if (File.Exists(winMergePath) && TryLaunchProcess(winMergePath, $"/e /u \"{originalPath}\" \"{checkerPath}\""))
                {
                    return; // Success, WinMerge (64-bit) launched.
                }
                if (TryLaunchProcess("WinMergeU.exe", $"/e /u \"{originalPath}\" \"{checkerPath}\""))
                {
                    return; // Success, WinMerge (found in PATH) launched.
                }
                if (File.Exists(vsCodePath) && TryLaunchProcess(vsCodePath, $"--diff \"{originalPath}\" \"{checkerPath}\""))
                {
                    return; // Success, VS Code launched.
                }
                if (TryLaunchProcess("code.exe", $"--diff \"{originalPath}\" \"{checkerPath}\""))
                {
                    return; // Success, VS Code (found in PATH) launched.
                }

                // If no automated tool could be launched, provide a fallback message with the paths to the temp files.
                string fallbackMessage = "Could not automatically launch a visual diff tool.\n\n" +
                                         "You can compare the files manually using your preferred tool.\n\n" +
                                         $"Original Project File:\n{originalPath}\n\n" +
                                         $"Checker Project File:\n{checkerPath}";
                MessageBox.Show(fallbackMessage, "Visual Diff Tool Not Found", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                // Catch any other unexpected errors during file writing or process starting.
                MessageBox.Show($"An unexpected error occurred while preparing the diff files.\n\nError: {ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Attempts to start a process and handles exceptions if the process is not found.
        /// </summary>
        /// <param name="fileName">The name of the executable to start.</param>
        /// <param name="args">The arguments to pass to the executable.</param>
        /// <returns>True if the process was started successfully, otherwise false.</returns>
        private bool TryLaunchProcess(string fileName, string args)
        {
            try
            {
                Process.Start(fileName, args);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // This specific exception is thrown when the executable is not found in the specified path or the system's PATH.
                return false;
            }
            catch (Exception)
            {
                // Catch any other potential exceptions during process start.
                return false;
            }
        }

        /// <summary>
        /// A private helper class to create a clean copy of a project for serialization.
        /// This is used to ensure that runtime-only properties (like FilePath) are not included
        /// in the temporary JSON files created for the visual diff.
        /// </summary>
        private class ScratchCardProjectCopy
        {
            public JobSettings Settings { get; set; }
            public SecuritySettings Security { get; set; }
            public ObservableCollection<PrizeTier> PrizeTiers { get; set; }
            public ObservableCollection<Symbol> AvailableSymbols { get; set; }
            public ObservableCollection<Symbol> NumericSymbols { get; set; }
            public CardLayout Layout { get; set; }

            public ScratchCardProjectCopy(ScratchCardProject source)
            {
                // A simple and effective way to perform a deep copy is to serialize the source object
                // and then deserialize it into a new instance.
                var options = new JsonSerializerOptions();
                string json = JsonSerializer.Serialize(source, options);
                var temp = JsonSerializer.Deserialize<ScratchCardProject>(json, options);

                // Copy the relevant properties from the temporary deserialized object.
                this.Settings = temp.Settings;
                this.Security = temp.Security;
                this.PrizeTiers = temp.PrizeTiers;
                this.AvailableSymbols = temp.AvailableSymbols;
                this.NumericSymbols = temp.NumericSymbols;
                this.Layout = temp.Layout;
            }
        }
        #endregion
    }
}
