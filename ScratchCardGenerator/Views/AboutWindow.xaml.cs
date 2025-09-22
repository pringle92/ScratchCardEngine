#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.Reflection;
using System.Windows;

#endregion

namespace ScratchCardGenerator.Views
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml. This window displays information
    /// about the application, including its version, copyright, and acknowledgements for third-party libraries.
    /// </summary>
    public partial class AboutWindow : Window
    {
        #region Public Properties

        /// <summary>
        /// Gets the version number of the executing assembly (e.g., "1.2.3").
        /// This property is bound to by a TextBlock in the XAML to display the current version dynamically.
        /// </summary>
        public string AssemblyVersion { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="AboutWindow"/> class.
        /// </summary>
        public AboutWindow()
        {
            InitializeComponent();

            // Retrieve the application's version dynamically from the executing assembly's metadata.
            // We only take the Major, Minor, and Build parts, omitting the Revision for a cleaner display.
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AssemblyVersion = $"{version.Major}.{version.Minor}.{version.Build}";

            // Set the DataContext of the window to itself. This is a simple pattern that allows the XAML
            // to directly bind to properties defined in this code-behind file, like the AssemblyVersion property.
            this.DataContext = this;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the Click event for the "OK" button.
        /// </summary>
        /// <param name="sender">The source of the event (the Button).</param>
        /// <param name="e">An object that contains no event data.</param>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Closes the About window when the OK button is clicked.
            this.Close();
        }

        #endregion
    }
}