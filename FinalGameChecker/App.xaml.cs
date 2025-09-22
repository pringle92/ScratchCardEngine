#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using ScratchCardGenerator.Common.Services;
using System.Configuration;
using System.Data;
using System.Windows;

#endregion

namespace FinalGameChecker
{
    /// <summary>
    /// Interaction logic for App.xaml. This is the main application class, which serves
    /// as the entry point for the WPF application.
    /// </summary>
    public partial class App : Application
    {
        #region Overridden Methods

        /// <summary>
        /// Overrides the OnStartup method, which is called when the application is launched.
        /// This is the ideal place for application-level initialisation tasks that need to run
        /// before any windows are shown.
        /// </summary>
        /// <param name="e">The startup event arguments.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Apply the visual theme (Light/Dark) as the very first step.
            // This ensures that all windows, starting with the MainWindow, are created
            // with the correct styles and resources already loaded.
            ThemeManager.ApplyTheme();
        }

        #endregion
    }
}
