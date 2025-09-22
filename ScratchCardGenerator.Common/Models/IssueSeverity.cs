#region Usings

#endregion

namespace ScratchCardGenerator.Common.Models
{
    #region Issue Severity Enum

    /// <summary>
    /// Defines the severity levels for a validation issue, allowing for differentiation
    /// between critical errors and informational warnings.
    /// </summary>
    public enum IssueSeverity
    {
        /// <summary>
        /// Represents a critical error that will likely cause file generation to fail.
        /// </summary>
        Error,

        /// <summary>
        /// Represents a non-critical issue or a suggestion for improvement that will not
        /// block generation but should be reviewed by the user.
        /// </summary>
        Warning
    }

    #endregion
}