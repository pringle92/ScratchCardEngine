#region Usings

using ScratchCardGenerator.Common.ViewModels;
using System.Collections.Generic;

#endregion

namespace ScratchCardGenerator.Common.Services
{
    #region Undo/Redo Service

    /// <summary>
    /// A service that provides a robust Undo/Redo functionality based on the Command pattern.
    /// It manages the history of executed commands, allowing for sequential undo and redo operations.
    /// </summary>
    /// <remarks>
    /// This class inherits from ViewModelBase to implement INotifyPropertyChanged. This allows UI elements
    /// (like Undo/Redo buttons) to bind to the CanUndo/CanRedo properties and automatically
    /// enable or disable themselves based on the state of the command stacks.
    /// </remarks>
    public class UndoRedoService : ViewModelBase
    {
        #region Private Fields

        /// <summary>
        /// The history of executed commands that can be undone. A Stack is used because we always
        /// want to undo the most recent action (Last-In, First-Out).
        /// </summary>
        private readonly Stack<IUndoableCommand> _undoStack = new Stack<IUndoableCommand>();

        /// <summary>
        /// The history of undone commands that can be redone.
        /// </summary>
        private readonly Stack<IUndoableCommand> _redoStack = new Stack<IUndoableCommand>();

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets a value indicating whether an undo operation is currently available.
        /// This is used to enable or disable the Undo UI button/menu item.
        /// </summary>
        public bool CanUndo => _undoStack.Count > 0;

        /// <summary>
        /// Gets a value indicating whether a redo operation is currently available.
        /// This is used to enable or disable the Redo UI button/menu item.
        /// </summary>
        public bool CanRedo => _redoStack.Count > 0;

        #endregion

        #region Public Methods

        /// <summary>
        /// Executes a new command and adds it to the undo history.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public void ExecuteCommand(IUndoableCommand command)
        {
            // First, perform the action.
            command.Execute();

            // Add the action to the undo stack so it can be reversed.
            _undoStack.Push(command);

            // A critical rule of Undo/Redo: any new action clears the redo history.
            // This prevents a confusing, non-linear history.
            _redoStack.Clear();

            // Notify the UI that the state has changed.
            UpdateCanExecute();
        }

        /// <summary>
        /// Undoes the most recent command.
        /// </summary>
        public void Undo()
        {
            if (CanUndo)
            {
                // Move the command from the undo stack to the redo stack.
                var command = _undoStack.Pop();
                _redoStack.Push(command);

                // Reverse the action.
                command.Undo();

                // Notify the UI that the state has changed.
                UpdateCanExecute();
            }
        }

        /// <summary>
        /// Redoes the most recently undone command.
        /// </summary>
        public void Redo()
        {
            if (CanRedo)
            {
                // Move the command from the redo stack back to the undo stack.
                var command = _redoStack.Pop();
                _undoStack.Push(command);

                // Re-apply the action.
                command.Execute();

                // Notify the UI that the state has changed.
                UpdateCanExecute();
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Notifies the data binding system that the CanUndo and CanRedo properties may have changed.
        /// This causes the UI to re-evaluate the enabled/disabled state of the Undo/Redo buttons.
        /// </summary>
        private void UpdateCanExecute()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        }

        #endregion
    }

    #endregion
}