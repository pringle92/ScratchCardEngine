#region Usings

// #region Usings: Specifies the namespaces that the class depends on.
using ScratchCardGenerator.Common.Adorners;
using ScratchCardGenerator.Common.Models;
using ScratchCardGenerator.Common.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

#endregion

namespace ScratchCardGenerator.Common.Views
{
    /// <summary>
    /// Interaction logic for ProjectEditorControl.xaml.
    /// This code-behind file contains UI-specific logic for the editor, primarily for handling
    /// complex user interactions like drag-and-drop from the palette to the designer canvas,
    /// moving items on the canvas, and displaying alignment guides.
    /// </summary>
    public partial class ProjectEditorControl : UserControl
    {
        #region Private Fields for Drag-and-Drop

        /// <summary>
        /// Stores the starting point of a mouse drag operation to determine if a drag has occurred.
        /// </summary>
        private Point _startPoint;

        /// <summary>
        /// A reference to the visual adorner that is displayed while an item is being dragged.
        /// </summary>
        private DragAdorner _dragAdorner;

        /// <summary>
        /// A reference to the data object (the GameModule) that is being dragged.
        /// </summary>
        private object _draggedData;

        #endregion

        #region Private Fields for Designer Canvas Enhancements

        /// <summary>
        /// The size of the grid to which items will snap on the designer canvas.
        /// </summary>
        private const int GridSize = 10;

        /// <summary>
        /// The distance (in pixels) within which an item will snap to an alignment guide or grid line.
        /// </summary>
        private const double SnapThreshold = 10.0;

        /// <summary>
        /// The canvas that serves as the layout area for game modules in the designer.
        /// </summary>
        private Canvas _cardLayoutCanvas;

        /// <summary>
        /// The visual adorner used for resizing elements.
        /// </summary>
        private ResizingAdorner _resizingAdorner;

        /// <summary>
        /// The currently selected element in the designer.
        /// </summary>
        private FrameworkElement _selectedElement;
        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="ProjectEditorControl"/> class.
        /// </summary>
        public ProjectEditorControl()
        {
            InitializeComponent();

            // Hook into the DataContextChanged event to know when the ViewModel is ready.
            this.DataContextChanged += ProjectEditorControl_DataContextChanged;

            // We subscribe to the Loaded event to ensure we don't try to find the canvas before it exists.
            this.Loaded += ProjectEditorControl_Loaded;
        }

        #endregion

        #region Adorner Management (NEW)

        /// <summary>
        /// Handles the event when the DataContext (our ViewModel) is assigned to this control.
        /// </summary>
        private void ProjectEditorControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from the old ViewModel if it exists.
            if (e.OldValue is ProjectEditorViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }

            // Subscribe to the new ViewModel's PropertyChanged event to monitor for selection changes.
            if (e.NewValue is ProjectEditorViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        /// <summary>
        /// NEW: Handles the Loaded event for the entire UserControl. This is our chance to
        /// find the dynamically-generated Canvas from the ItemsControl template.
        /// </summary>
        private void ProjectEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            // The canvas is the ItemsHost panel inside the ItemsControl. We use a helper
            // method to reliably find it within the control's visual tree.
            _cardLayoutCanvas = FindVisualChild<Canvas>(GameModulesItemsControl);
        }

        /// <summary>
        /// Listens for property changes on the ViewModel, specifically for the 'SelectedModule'.
        /// </summary>
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // We only care when the selected module changes.
            if (e.PropertyName == nameof(ProjectEditorViewModel.SelectedModule))
            {
                UpdateAdorners();
            }
        }

        /// <summary>
        /// Adds or removes the resizing adorner based on the currently selected game module in the ViewModel.
        /// This method is the core of the adorner management logic.
        /// </summary>
        private void UpdateAdorners()
        {
            if (_cardLayoutCanvas == null) return;
            var adornerLayer = AdornerLayer.GetAdornerLayer(_cardLayoutCanvas);
            if (adornerLayer == null) return;

            if (_selectedElement != null && _resizingAdorner != null)
            {
                adornerLayer.Remove(_resizingAdorner);
                _resizingAdorner = null;
                _selectedElement = null;
            }

            if (DataContext is ProjectEditorViewModel vm && vm.SelectedModule != null)
            {
                var container = _cardLayoutCanvas.Children.OfType<ContentPresenter>()
                                  .FirstOrDefault(cp => cp.Content == vm.SelectedModule);

                if (container != null)
                {
                    _selectedElement = container;
                    // MODIFIED: Pass the canvas into the adorner's constructor.
                    _resizingAdorner = new ResizingAdorner(_selectedElement, _cardLayoutCanvas);
                    adornerLayer.Add(_resizingAdorner);
                }
            }
        }
        #endregion

        #region Drag-and-Drop and Module Selection Logic

        /// <summary>
        /// Stores the initial mouse position when the user presses the left mouse button on the game module palette.
        /// </summary>
        private void Palette_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        /// <summary>
        /// Initiates a drag-and-drop operation if the mouse is moved a sufficient distance while the left button is pressed.
        /// </summary>
        private void Palette_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Proceed only if the left mouse button is held down.
            if (e.LeftButton == MouseButtonState.Pressed && sender is ListBox parent)
            {
                Point position = e.GetPosition(null);

                // Check if the mouse has moved far enough from the start point to be considered a drag, not just a click.
                if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Find the ListBoxItem being dragged.
                    if (parent.InputHitTest(e.GetPosition(parent)) is FrameworkElement element &&
                        element.DataContext is GameModule selectedModule)
                    {
                        // Set up the data to be dragged.
                        _draggedData = selectedModule;
                        var data = new DataObject(typeof(GameModule), _draggedData);

                        // Create and show the visual adorner that follows the mouse cursor.
                        if (_dragAdorner == null)
                        {
                            var adornerLayer = AdornerLayer.GetAdornerLayer(RootControl);
                            _dragAdorner = new DragAdorner(RootControl, _draggedData, GetDragDropTemplate());
                            adornerLayer.Add(_dragAdorner);
                        }

                        // Start the built-in WPF drag-and-drop operation.
                        DragDrop.DoDragDrop(parent, data, DragDropEffects.Copy);

                        // Clean up the adorner after the operation is complete.
                        if (_dragAdorner != null)
                        {
                            AdornerLayer.GetAdornerLayer(RootControl).Remove(_dragAdorner);
                            _dragAdorner = null;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles the DragEnter event for the canvas, setting the mouse cursor to indicate a 'Copy' operation is possible.
        /// </summary>
        private void CardLayoutCanvas_DragEnter(object sender, DragEventArgs e)
        {
            // Check if the data being dragged is of the expected GameModule type.
            e.Effects = e.Data.GetDataPresent(typeof(GameModule)) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        /// <summary>
        /// Updates the position of the drag adorner to follow the mouse cursor as it moves over the grid.
        /// </summary>
        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            if (_dragAdorner != null)
            {
                _dragAdorner.SetPosition(e.GetPosition(this));
            }
        }

        /// <summary>
        /// Handles the Drop event on the canvas, creating a new game module when an item is dropped.
        /// </summary>
        private void CardLayoutCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(GameModule)) is GameModule droppedModule &&
                DataContext is ProjectEditorViewModel viewModel && sender is Canvas canvas)
            {
                // It's crucial to create a deep clone of the module from the palette.
                // This ensures the new module on the canvas is a completely separate object.
                var newModule = viewModel.DeepCloneModule(droppedModule);
                if (newModule == null) return;

                // Calculate the position for the new module, centering it on the mouse cursor.
                Point position = e.GetPosition(canvas);
                position.X -= (newModule.Size.Width / 2);
                position.Y -= (newModule.Size.Height / 2);
                newModule.Position = position;

                // Add the new module to the project's collection, which updates the UI.
                viewModel.CurrentProject.Layout.GameModules.Add(newModule);
                viewModel.SelectedModule = newModule; // Automatically select the new module.
            }
        }

        /// <summary>
        /// Handles the mouse down event on an existing game module on the canvas to select it and prepare for moving.
        /// </summary>
        private void ItemContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ContentPresenter container && DataContext is ProjectEditorViewModel viewModel)
            {
                // Set the clicked module as the selected one in the ViewModel.
                viewModel.SelectedModule = container.Content as GameModule;

                // Store the start point of the drag relative to the module itself.
                _startPoint = e.GetPosition(container);

                // Capture the mouse to ensure mouse move events are received even if the cursor leaves the element.
                container.CaptureMouse();
                e.Handled = true; // Mark the event as handled to prevent it from bubbling further.
            }
        }

        /// <summary>
        /// Handles a mouse click directly on the canvas background to deselect any selected module.
        /// </summary>
        private void DesignerCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Check if the source of the click was the Canvas itself, not one of the items on it.
            if (e.Source.GetType() == typeof(Canvas) && DataContext is ProjectEditorViewModel viewModel)
            {
                viewModel.SelectedModule = null;
            }
        }

        /// <summary>
        /// Handles moving an existing game module on the canvas, including snapping logic.
        /// </summary>
        private void CardLayoutCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // This logic only runs if a ContentPresenter (a game module) has captured the mouse.
            if (Mouse.Captured is ContentPresenter container && container.Content is GameModule draggedModule &&
                e.LeftButton == MouseButtonState.Pressed && sender is Canvas canvas)
            {
                // --- Snapping and Alignment Guide Logic ---
                Point currentPosition = e.GetPosition(canvas);
                double newX = currentPosition.X - _startPoint.X;
                double newY = currentPosition.Y - _startPoint.Y;

                // Hide guides by default.
                HorizontalGuide.Visibility = Visibility.Collapsed;
                VerticalGuide.Visibility = Visibility.Collapsed;

                bool isSnappedX = false;
                bool isSnappedY = false;

                // Check for snapping opportunities against other modules on the canvas.
                if (DataContext is ProjectEditorViewModel viewModel)
                {
                    var otherModules = viewModel.CurrentProject.Layout.GameModules.Where(m => m != draggedModule);
                    foreach (var otherModule in otherModules)
                    {
                        // Check for vertical alignment (left, right, center edges).
                        if (!isSnappedX)
                        {
                            if (Math.Abs(newX - otherModule.Position.X) < SnapThreshold)
                            { newX = otherModule.Position.X; isSnappedX = true; ShowVerticalGuide(newX, canvas.ActualHeight); }
                            else if (Math.Abs((newX + draggedModule.Size.Width) - (otherModule.Position.X + otherModule.Size.Width)) < SnapThreshold)
                            { newX = otherModule.Position.X + otherModule.Size.Width - draggedModule.Size.Width; isSnappedX = true; ShowVerticalGuide(newX + draggedModule.Size.Width, canvas.ActualHeight); }
                            else if (Math.Abs((newX + draggedModule.Size.Width / 2) - (otherModule.Position.X + otherModule.Size.Width / 2)) < SnapThreshold)
                            { newX = otherModule.Position.X + otherModule.Size.Width / 2 - draggedModule.Size.Width / 2; isSnappedX = true; ShowVerticalGuide(newX + draggedModule.Size.Width / 2, canvas.ActualHeight); }
                        }
                        // Check for horizontal alignment (top, bottom, middle edges).
                        if (!isSnappedY)
                        {
                            if (Math.Abs(newY - otherModule.Position.Y) < SnapThreshold)
                            { newY = otherModule.Position.Y; isSnappedY = true; ShowHorizontalGuide(newY, canvas.ActualWidth); }
                            else if (Math.Abs((newY + draggedModule.Size.Height) - (otherModule.Position.Y + otherModule.Size.Height)) < SnapThreshold)
                            { newY = otherModule.Position.Y + otherModule.Size.Height - draggedModule.Size.Height; isSnappedY = true; ShowHorizontalGuide(newY + draggedModule.Size.Height, canvas.ActualHeight); }
                            else if (Math.Abs((newY + draggedModule.Size.Height / 2) - (otherModule.Position.Y + otherModule.Size.Height / 2)) < SnapThreshold)
                            { newY = otherModule.Position.Y + otherModule.Size.Height / 2 - draggedModule.Size.Height / 2; isSnappedY = true; ShowHorizontalGuide(newY + draggedModule.Size.Height / 2, canvas.ActualWidth); }
                        }
                        if (isSnappedX && isSnappedY) break; // Stop checking once snapped on both axes.
                    }
                }

                // --- Grid Snapping Logic ---
                // If the module hasn't snapped to another element, try to snap it to the background grid.
                if (!isSnappedX)
                {
                    double remX = newX % GridSize;
                    if (remX < SnapThreshold) newX -= remX;
                    else if (GridSize - remX < SnapThreshold) newX += (GridSize - remX);
                }
                if (!isSnappedY)
                {
                    double remY = newY % GridSize;
                    if (remY < SnapThreshold) newY -= remY;
                    else if (GridSize - remY < SnapThreshold) newY += (GridSize - remY);
                }

                // --- Boundary Enforcement ---
                // Prevent the module from being dragged outside the canvas bounds.
                newX = Math.Max(0, newX);
                newY = Math.Max(0, newY);
                newX = Math.Min(canvas.ActualWidth - draggedModule.Size.Width, newX);
                newY = Math.Min(canvas.ActualHeight - draggedModule.Size.Height, newY);

                // Update the module's position in the model, which will move it on the canvas via data binding.
                draggedModule.Position = new Point(newX, newY);
            }
        }

        /// <summary>
        /// Handles the mouse up event to finalise the moving of a game module.
        /// </summary>
        private void ItemContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ContentPresenter container)
            {
                // Hide the alignment guides and release the mouse capture.
                HorizontalGuide.Visibility = Visibility.Collapsed;
                VerticalGuide.Visibility = Visibility.Collapsed;
                container.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        #endregion

        #region Context Menu and Other UI Logic

        /// <summary>
        /// Handles the Click event for the "Remove" item in a game module's context menu.
        /// </summary>
        private void RemoveModule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && DataContext is ProjectEditorViewModel viewModel)
            {
                // The DataContext of the MenuItem will be the GameModule it belongs to.
                if (menuItem.DataContext is GameModule moduleToRemove)
                {
                    viewModel.CurrentProject.Layout.GameModules.Remove(moduleToRemove);
                }
            }
        }

        /// <summary>
        /// Handles the CellEditEnding event of the DataGrids to trigger a recalculation of the analysis.
        /// </summary>
        public void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is ProjectEditorViewModel viewModel)
            {
                // Use the dispatcher to invoke the recalculation. This ensures that the data binding
                // has had time to update the model before the calculation runs.
                Dispatcher.BeginInvoke(new Action(() => viewModel.RecalculateAnalysis()), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Handles the GotKeyboardFocus event for TextBoxes to select all text when tabbing into them.
        /// This improves keyboard navigation usability.
        /// </summary>
        private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Only select all if the focus was gained via the Tab key.
                // This preserves the default click-to-place-cursor behaviour for mouse users.
                if (e.KeyboardDevice.IsKeyDown(Key.Tab))
                {
                    textBox.SelectAll();
                }
            }
        }

        #endregion

        #region Alignment Guide and Adorner Helper Methods

        /// <summary>
        /// Displays the horizontal alignment guide at a specific Y-coordinate.
        /// </summary>
        private void ShowHorizontalGuide(double y, double width)
        {
            HorizontalGuide.X1 = 0;
            HorizontalGuide.Y1 = y;
            HorizontalGuide.X2 = width;
            HorizontalGuide.Y2 = y;
            HorizontalGuide.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Displays the vertical alignment guide at a specific X-coordinate.
        /// </summary>
        private void ShowVerticalGuide(double x, double height)
        {
            VerticalGuide.X1 = x;
            VerticalGuide.Y1 = 0;
            VerticalGuide.X2 = x;
            VerticalGuide.Y2 = height;
            VerticalGuide.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Retrieves the DataTemplate used for rendering the drag-and-drop adorner.
        /// </summary>
        private DataTemplate GetDragDropTemplate()
        {
            return Resources["DragDropPreviewTemplate"] as DataTemplate;
        }

        #endregion

        #region DragAdorner Class

        /// <summary>
        /// A private nested class that defines a custom Adorner.
        /// An Adorner is a lightweight visual element that can be rendered in a layer above the main UI,
        /// perfect for creating a "ghost" image of an item being dragged.
        /// </summary>
        private class DragAdorner : Adorner
        {
            private readonly ContentPresenter _contentPresenter;
            private double _left;
            private double _top;

            /// <summary>
            /// Initialises a new instance of the DragAdorner class.
            /// </summary>
            public DragAdorner(UIElement adornedElement, object data, DataTemplate template) : base(adornedElement)
            {
                _contentPresenter = new ContentPresenter
                {
                    Content = data,
                    ContentTemplate = template,
                    Opacity = 0.7
                };
                IsHitTestVisible = false; // The adorner should not interfere with mouse events.
            }

            /// <summary>
            /// Sets the position of the adorner.
            /// </summary>
            public void SetPosition(Point position)
            {
                _left = position.X;
                _top = position.Y;
                if (Parent is AdornerLayer layer)
                {
                    layer.Update(AdornedElement); // Force a re-render of the adorner layer.
                }
            }

            protected override Size MeasureOverride(Size constraint)
            {
                _contentPresenter.Measure(constraint);
                return _contentPresenter.DesiredSize;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                _contentPresenter.Arrange(new Rect(finalSize));
                return finalSize;
            }

            protected override Visual GetVisualChild(int index) => _contentPresenter;
            protected override int VisualChildrenCount => 1;

            public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
            {
                var result = new GeneralTransformGroup();
                result.Children.Add(base.GetDesiredTransform(transform));
                result.Children.Add(new TranslateTransform(_left, _top)); // Apply the position offset.
                return result;
            }
        }

        #endregion

        // --- NEW METHOD ---
        #region Data Grid Keyboard Handling

        /// <summary>
        /// Intercepts the PreviewKeyDown event on the DataGrids to handle the Delete key.
        /// This ensures that pressing Delete executes our custom command with its confirmation
        /// logic, rather than using the DataGrid's default direct-deletion behavior.
        /// </summary>
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // We only care about the Delete key.
            if (e.Key == Key.Delete)
            {
                if (sender is DataGrid grid && DataContext is ProjectEditorViewModel viewModel)
                {
                    // Get the currently selected item from the grid that triggered the event.
                    var selectedItem = grid.SelectedItem;
                    if (selectedItem == null) return;

                    // Determine which command to execute based on the name of the grid.
                    if (grid.Name == "PrizesGrid")
                    {
                        viewModel.RemovePrizeTierCommand.Execute(selectedItem);
                    }
                    else if (grid.Name == "SymbolsGrid")
                    {
                        viewModel.RemoveSymbolCommand.Execute(selectedItem);
                    }
                    else if (grid.Name == "GameSymbolsGrid")
                    {
                        viewModel.RemoveNumericSymbolCommand.Execute(selectedItem);
                    }

                    // Mark the event as handled to prevent the DataGrid's default
                    // delete command from also running.
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Visual Tree Helper

        /// <summary>
        /// A helper method that recursively searches the visual tree for a child element of a specific type.
        /// This is necessary to find elements that are generated inside a template, as they are not
        /// directly accessible as named fields in the code-behind.
        /// </summary>
        /// <typeparam name="T">The type of the child element to find.</typeparam>
        /// <param name="parent">The parent element to start the search from.</param>
        /// <returns>The first child of the specified type that is found, or null if no such child exists.</returns>
        private static T FindVisualChild<T>(DependencyObject parent) where T : Visual
        {
            // Return null if the parent is null to prevent exceptions.
            if (parent == null) return null;

            // Iterate through all direct children of the parent element.
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                // Get the child at the current index.
                var child = VisualTreeHelper.GetChild(parent, i);

                // Check if this child is of the type we are looking for.
                if (child is T typedChild)
                {
                    // If it is, we've found our element, so we return it.
                    return typedChild;
                }

                // If the child is not the type we're looking for, we then recursively call this
                // same method on the child, searching its children.
                var result = FindVisualChild<T>(child);

                // If the recursive call found the element, we pass the result up the call stack.
                if (result != null)
                {
                    return result;
                }
            }

            // If we've iterated through all children and their descendants without finding
            // the element, we return null.
            return null;
        }

        #endregion
    }
}