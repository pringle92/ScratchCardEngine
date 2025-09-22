#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using System.Collections.Generic;
using System.Collections.ObjectModel;

#endregion

namespace ScratchCardGenerator.Common.Models
{
    /// <summary>
    /// Represents the overall layout of a single scratch card, acting as a container
    /// for all the game modules placed on the designer canvas.
    /// </summary>
    public class CardLayout
    {
        #region Properties

        /// <summary>
        /// Gets or sets the collection of game modules that make up the scratch card layout.
        /// An ObservableCollection is used specifically for its integration with the WPF data binding system.
        /// It automatically notifies the UI when items are added, removed, or the collection is refreshed,
        /// allowing the designer canvas to update in real-time.
        /// </summary>
        public ObservableCollection<GameModule> GameModules { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="CardLayout"/> class.
        /// </summary>
        public CardLayout()
        {
            // The collection is initialised in the constructor to ensure it is never null,
            // preventing NullReferenceExceptions elsewhere in the application.
            GameModules = new ObservableCollection<GameModule>();
        }

        #endregion
    }
}