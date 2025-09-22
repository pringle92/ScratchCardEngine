#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System;
using System.Windows.Input;

#endregion

namespace ScratchCardGenerator.Common.ViewModels.Commands
{
    /// <summary>
    /// A standard, reusable implementation of the ICommand interface for synchronous operations.
    /// This class allows for binding UI actions (like button clicks) to methods (delegates) in the ViewModel,
    /// which is a core tenet of the MVVM design pattern.
    /// </summary>
    public class RelayCommand : ICommand
    {
        #region Private Fields

        /// <summary>
        /// A delegate that holds the method to be executed when the command is invoked.
        /// </summary>
        private readonly Action<object> _execute;

        /// <summary>
        /// A delegate that holds the method used to determine if the command can be executed in its current state.
        /// This can be null, in which case the command is always considered executable.
        /// </summary>
        private readonly Predicate<object> _canExecute;

        #endregion

        #region Constructors

        /// <summary>
        /// Initialises a new instance of the <see cref="RelayCommand"/> class.
        /// </summary>
        /// <param name="execute">The execution logic. This delegate is required.</param>
        /// <param name="canExecute">The execution status logic. This delegate is optional.</param>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            // The execute delegate is essential for the command to function.
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #endregion

        #region ICommand Members

        /// <summary>
        /// Defines the method that determines whether the command can execute in its current state.
        /// This is used by WPF to automatically enable or disable UI controls bound to this command.
        /// </summary>
        /// <param name="parameter">Data used by the command. This object is passed from the UI, often via a CommandParameter.</param>
        /// <returns>True if this command can be executed; otherwise, false.</returns>
        public bool CanExecute(object parameter)
        {
            // If no canExecute delegate was provided, the command is always executable.
            // Otherwise, invoke the provided delegate to determine the state.
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// Occurs when changes occur that affect whether the command should execute.
        /// </summary>
        /// <remarks>
        /// This event is wired into the WPF CommandManager's RequerySuggested event.
        /// This is a built-in WPF mechanism that automatically triggers a re-evaluation of CanExecute
        /// on common UI events, such as focus changes or text input, without manual intervention.
        /// </remarks>
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Defines the method to be called when the command is invoked by a UI action.
        /// </summary>
        /// <param name="parameter">Data used by the command, passed from the UI's CommandParameter.</param>
        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Manually raises the CanExecuteChanged event to force the command's state to be re-evaluated by the UI.
        /// This is useful when the CanExecute predicate depends on a property that changes outside of the typical UI event cycle.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            // Invalidating the RequerySuggested event on the CommandManager effectively tells WPF
            // to re-query the CanExecute status of all commands.
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion
    }
}
