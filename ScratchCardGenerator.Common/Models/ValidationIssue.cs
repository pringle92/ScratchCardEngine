#region Usings

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Validation Issue Class

    /// <summary>
    /// A data model that represents a single validation issue found within a scratch card project.
    /// This is used to display a list of problems to the user in the UI.
    /// </summary>
    public class ValidationIssue
    {
        #region Properties

        /// <summary>
        /// Gets or sets the severity of the issue (Error or Warning).
        /// </summary>
        public IssueSeverity Severity { get; set; }

        /// <summary>
        /// Gets or sets the descriptive message explaining the issue to the user.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets a reference to the source object that has the issue (e.g., a specific GameModule).
        /// This can be used in the future to implement "click to navigate" functionality.
        /// </summary>
        public object Source { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="ValidationIssue"/> class.
        /// </summary>
        /// <param name="message">The descriptive message.</param>
        /// <param name="severity">The severity of the issue.</param>
        /// <param name="source">The source object causing the issue.</param>
        public ValidationIssue(string message, IssueSeverity severity, object source = null)
        {
            Message = message;
            Severity = severity;
            Source = source;
        }

        #endregion
    }

    #endregion
}