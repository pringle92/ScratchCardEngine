#region Usings

// No specific usings are required for a simple interface definition.

#endregion

namespace ScratchCardGenerator.Common.Services
{
    #region IUndoableCommand Interface

    /// <summary>
    /// Defines the essential contract for an action that supports Undo and Redo functionality.
    /// This interface is the cornerstone of the Command design pattern implementation, ensuring that
    /// any operation that can be undone provides a consistent mechanism for execution and reversal.
    /// </summary>
    public interface IUndoableCommand
    {
        #region Methods

        /// <summary>
        /// Executes the command's primary action. This method is called when the action is first performed
        /// or when it is redone from the redo stack.
        /// </summary>
        void Execute();

        /// <summary>
        /// Reverses the command's primary action. This method is called when the user performs an "Undo" operation.
        /// It must restore the application state to precisely what it was before the Execute method was called.
        /// </summary>
        void Undo();

        #endregion
    }

    #endregion
}