#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.ComponentModel;
using System.Runtime.CompilerServices;

#endregion

namespace ScratchCardGenerator.Common.ViewModels
{
    /// <summary>
    /// A base class for ViewModels that implements the INotifyPropertyChanged interface.
    /// This provides a standard, reusable way for properties to notify the UI when their values change,
    /// which is fundamental to the MVVM pattern in WPF.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes. The UI's binding system subscribes to this event.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event for a specific property, notifying the UI to update.
        /// </summary>
        /// <param name="propertyName">
        /// The name of the property that changed.
        /// The [CallerMemberName] attribute makes this parameter optional, as the compiler will automatically provide the name of the calling property.
        /// </param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Invoke the event handler if it's not null, passing this object as the sender and the property name in the event arguments.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
