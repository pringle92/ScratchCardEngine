#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

#endregion

namespace ScratchCardGenerator.Common.ViewModels.Commands
{
    /// <summary>
    /// An implementation of ICommand that supports asynchronous operations, preventing the UI from freezing during long-running tasks.
    /// This command manages its execution state and notifies the UI when it is running, which can be used to disable controls
    /// automatically while the asynchronous operation is in progress, preventing re-entrant calls.
    /// </summary>
    public class AsyncRelayCommand : ICommand, INotifyPropertyChanged
    {
        #region Private Fields

        /// <summary>
        /// A delegate that holds the asynchronous method to be executed when the command is invoked.
        /// It must return a Task.
        /// </summary>
        private readonly Func<Task> _execute;

        /// <summary>
        /// A delegate that holds the method used to determine if the command can be executed.
        /// </summary>
        private readonly Func<bool> _canExecute;

        /// <summary>
        /// The backing field for the IsExecuting property.
        /// </summary>
        private bool _isExecuting;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="AsyncRelayCommand"/> class.
        /// </summary>
        /// <param name="execute">The asynchronous action to execute. This delegate is required.</param>
        /// <param name="canExecute">A function to determine if the command can execute. This delegate is optional.</param>
        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the command is currently executing its asynchronous task.
        /// This property can be bound to in the UI (e.g., to an IsEnabled property) to provide visual feedback.
        /// </summary>
        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting == value) return; // Avoid unnecessary updates.
                _isExecuting = value;
                OnPropertyChanged();
                // When the execution state changes, the result of CanExecute may also have changed.
                // We must notify the CommandManager to re-evaluate the command's status.
                RaiseCanExecuteChanged();
            }
        }

        #endregion

        #region ICommand Implementation

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// The command cannot execute if its asynchronous task is already running.
        /// </summary>
        /// <param name="parameter">Data used by the command (not used in this implementation).</param>
        /// <returns>True if this command can be executed; otherwise, false.</returns>
        public bool CanExecute(object parameter)
        {
            // The command is only executable if it is not already running AND its optional canExecute delegate returns true.
            return !IsExecuting && (_canExecute == null || _canExecute());
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked.
        /// </summary>
        /// <remarks>
        /// This method is marked 'async void'. This is a special pattern that should typically be avoided,
        /// but it is the correct and necessary approach for event handlers and ICommand.Execute implementations
        /// in UI frameworks like WPF, as the calling infrastructure does not await a returned Task.
        /// </remarks>
        /// <param name="parameter">Data used by the command (not used in this implementation).</param>
        public async void Execute(object parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    IsExecuting = true;
                    await _execute();
                }
                finally
                {
                    // The 'finally' block is critical. It ensures that IsExecuting is always reset to false,
                    // even if the asynchronous _execute task fails with an exception.
                    IsExecuting = false;
                }
            }
        }

        /// <summary>
        /// Occurs when changes occur that affect whether or not the command should execute.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Raises the CanExecuteChanged event to re-evaluate the command's execution status.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event, used to notify the UI about changes to the IsExecuting property.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
