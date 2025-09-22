#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.Linq;
using System.Text.RegularExpressions;

#endregion

namespace FinalGameChecker.ViewModels
{
    /// <summary>
    /// A ViewModel that represents a single difference found during project comparison.
    /// It includes both a user-friendly description and the technical details of the discrepancy,
    /// and is designed to be displayed as a row in a ListView or DataGrid.
    /// </summary>
    public class DifferenceViewModel
    {
        #region Public Properties

        /// <summary>
        /// Gets the broad area where the difference was found (e.g., "Job Settings", "Prize Tiers").
        /// This is a user-friendly name derived from the technical path.
        /// </summary>
        public string Area { get; private set; }

        /// <summary>
        /// Gets the specific property that has a different value (e.g., "Cards Per Pack").
        /// </summary>
        public string Property { get; private set; }

        /// <summary>
        /// Gets the value of the property from the original project file.
        /// </summary>
        public string OriginalValue { get; set; }

        /// <summary>
        /// Gets the value of the property from the checker's recreated project file.
        /// </summary>
        public string CheckerValue { get; set; }

        /// <summary>
        /// Gets the full, technical property path from the comparison library for debugging purposes (e.g., "CheckerProject.Settings.CardsPerPack").
        /// </summary>
        public string TechnicalPath { get; private set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="DifferenceViewModel"/> class.
        /// </summary>
        /// <param name="technicalPath">The full property path from the comparison tool.</param>
        /// <param name="originalValue">The value in the original object.</param>
        /// <param name="checkerValue">The value in the checker's object.</param>
        public DifferenceViewModel(string technicalPath, string originalValue, string checkerValue)
        {
            TechnicalPath = technicalPath;
            OriginalValue = originalValue;
            CheckerValue = checkerValue;

            // Immediately translate the technical path into friendly names upon creation.
            TranslatePathToFriendlyNames();
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Translates the complex technical property path provided by the comparison library
        /// into user-friendly Area and Property names for clearer display in the UI.
        /// </summary>
        private void TranslatePathToFriendlyNames()
        {
            // Set default values in case no specific rule matches.
            Area = "Unknown Area";
            Property = TechnicalPath;

            // Use a regular expression to find collection indexers like "[12]".
            var match = Regex.Match(TechnicalPath, @"\[(\d+)\]");
            if (match.Success)
            {
                string index = match.Groups[1].Value;
                // Example technical path: "CheckerProject.PrizeTiers[5].LvwWinnerCount"
                if (TechnicalPath.Contains("PrizeTiers"))
                {
                    Area = $"Prize Tiers (Index: {index})";
                    Property = TechnicalPath.Split('.').LastOrDefault() ?? "Unknown";
                }
                else if (TechnicalPath.Contains("AvailableSymbols"))
                {
                    Area = $"Symbols (Index: {index})";
                    Property = TechnicalPath.Split('.').LastOrDefault() ?? "Unknown";
                }
                else if (TechnicalPath.Contains("NumericSymbols"))
                {
                    Area = $"Game Symbols (Index: {index})";
                    Property = TechnicalPath.Split('.').LastOrDefault() ?? "Unknown";
                }
                else if (TechnicalPath.Contains("GameModules"))
                {
                    Area = $"Card Layout (Game Module Index: {index})";
                    Property = TechnicalPath.Split('.').LastOrDefault() ?? "Unknown";
                }
            }
            // Handle properties within the Settings object.
            else if (TechnicalPath.Contains("Settings."))
            {
                Area = "Job Settings";
                Property = TechnicalPath.Replace("CheckerProject.Settings.", "");
            }
            // Handle properties within the Security object.
            else if (TechnicalPath.Contains("Security."))
            {
                Area = "Security Settings";
                Property = TechnicalPath.Replace("CheckerProject.Security.", "");
            }

            // Improve readability of property names (e.g., "CardsPerPack" becomes "Cards Per Pack")
            // by inserting a space before any capital letter that is not at the start of the string.
            Property = Regex.Replace(Property, "(\\B[A-Z])", " $1");
        }

        #endregion
    }
}
