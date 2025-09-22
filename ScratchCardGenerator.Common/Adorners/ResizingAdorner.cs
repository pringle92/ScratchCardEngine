#region Usings

using ScratchCardGenerator.Common.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

#endregion

namespace ScratchCardGenerator.Common.Adorners
{
    #region Resizing Adorner

    /// <summary>
    /// A custom Adorner that provides visual resizing handles and sophisticated snapping logic.
    /// It allows interactive resizing on a Canvas with visual guides for aligning to a grid,
    /// other elements' edges, and other elements' sizes.
    /// </summary>
    public class ResizingAdorner : Adorner
    {
        #region Private Fields

        // Thumbs for resizing handles.
        private readonly Thumb _topLeft, _topRight, _bottomLeft, _bottomRight;
        private readonly Thumb _left, _right, _top, _bottom;
        private readonly VisualCollection _visualChildren;

        // The canvas on which the adorned element resides, needed to find other elements to snap to.
        private readonly Canvas _parentCanvas;

        // Configuration for snapping behaviour.
        private const double MinModuleSize = 20;
        private const double SnapThreshold = 10.0;
        private const int GridSize = 10;

        // Fields to manage the state of the visual snap lines.
        private double? _snapLineX;
        private double? _snapLineY;
        private readonly Pen _snapLinePen;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="ResizingAdorner"/> class.
        /// </summary>
        /// <param name="adornedElement">The UIElement to which this adorner will be attached.</param>
        /// <param name="parentCanvas">A reference to the parent Canvas, used for snapping calculations.</param>
        public ResizingAdorner(UIElement adornedElement, Canvas parentCanvas) : base(adornedElement)
        {
            _parentCanvas = parentCanvas;
            _visualChildren = new VisualCollection(this);

            // Create a styled pen for drawing the snap lines.
            _snapLinePen = new Pen(new SolidColorBrush(Colors.Red), 1) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) };

            // Initialise each thumb and attach its event handlers.
            _topLeft = CreateThumb(Cursors.SizeNWSE);
            _topRight = CreateThumb(Cursors.SizeNESW);
            _bottomLeft = CreateThumb(Cursors.SizeNESW);
            _bottomRight = CreateThumb(Cursors.SizeNWSE);
            _left = CreateThumb(Cursors.SizeWE);
            _right = CreateThumb(Cursors.SizeWE);
            _top = CreateThumb(Cursors.SizeNS);
            _bottom = CreateThumb(Cursors.SizeNS);
        }

        #endregion

        #region Override Methods

        /// <summary>
        /// Overridden to provide custom rendering, in this case, to draw the visual snap lines.
        /// </summary>
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            // Draw a vertical snap line if one has been calculated.
            if (_snapLineX.HasValue)
            {
                drawingContext.DrawLine(_snapLinePen, new Point(_snapLineX.Value, -10000), new Point(_snapLineX.Value, 20000));
            }
            // Draw a horizontal snap line if one has been calculated.
            if (_snapLineY.HasValue)
            {
                drawingContext.DrawLine(_snapLinePen, new Point(-10000, _snapLineY.Value), new Point(20000, _snapLineY.Value));
            }
        }

        /// <summary>
        /// Overridden to define the layout of the adorner's visual children (the thumbs).
        /// </summary>
        protected override Size ArrangeOverride(Size finalSize)
        {
            double thumbSize = 10;
            double handleOffset = thumbSize / 2;

            _topLeft.Arrange(new Rect(-handleOffset, -handleOffset, thumbSize, thumbSize));
            _topRight.Arrange(new Rect(AdornedElement.RenderSize.Width - handleOffset, -handleOffset, thumbSize, thumbSize));
            _bottomLeft.Arrange(new Rect(-handleOffset, AdornedElement.RenderSize.Height - handleOffset, thumbSize, thumbSize));
            _bottomRight.Arrange(new Rect(AdornedElement.RenderSize.Width - handleOffset, AdornedElement.RenderSize.Height - handleOffset, thumbSize, thumbSize));
            _left.Arrange(new Rect(-handleOffset, AdornedElement.RenderSize.Height / 2 - handleOffset, thumbSize, thumbSize));
            _right.Arrange(new Rect(AdornedElement.RenderSize.Width - handleOffset, AdornedElement.RenderSize.Height / 2 - handleOffset, thumbSize, thumbSize));
            _top.Arrange(new Rect(AdornedElement.RenderSize.Width / 2 - handleOffset, -handleOffset, thumbSize, thumbSize));
            _bottom.Arrange(new Rect(AdornedElement.RenderSize.Width / 2 - handleOffset, AdornedElement.RenderSize.Height - handleOffset, thumbSize, thumbSize));

            return finalSize;
        }

        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// A factory method to create and configure a single resizing handle (Thumb).
        /// </summary>
        private Thumb CreateThumb(Cursor cursor)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                Width = 10,
                Height = 10,
                Background = new SolidColorBrush(Colors.DodgerBlue),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1)
            };

            thumb.DragStarted += Thumb_DragStarted;
            thumb.DragDelta += Thumb_DragDelta;
            thumb.DragCompleted += Thumb_DragCompleted;
            _visualChildren.Add(thumb);
            return thumb;
        }

        /// <summary>
        /// Handles the start of a drag operation to clear any old snap lines.
        /// </summary>
        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            _snapLineX = null;
            _snapLineY = null;
            this.InvalidateVisual(); // Redraw to clear lines.
        }

        /// <summary>
        /// Handles the end of a drag operation to clear the snap lines.
        /// </summary>
        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _snapLineX = null;
            _snapLineY = null;
            this.InvalidateVisual(); // Redraw to clear lines.
        }

        /// <summary>
        /// Handles the DragDelta event for all thumbs, performing all resizing and snapping calculations.
        /// </summary>
        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (AdornedElement is not FrameworkElement adornedElement) return;
            if (adornedElement.DataContext is not GameModule gameModule) return;

            // Get all other modules on the canvas to check for snapping.
            var otherModules = _parentCanvas.Children.OfType<ContentPresenter>()
                                 .Where(cp => cp.Content != gameModule)
                                 .Select(cp => new { Element = cp, Model = cp.Content as GameModule })
                                 .Where(item => item.Model != null)
                                 .ToList();

            // --- Calculate Potential New Dimensions ---
            double newLeft = gameModule.Position.X;
            double newTop = gameModule.Position.Y;
            double newWidth = gameModule.Size.Width;
            double newHeight = gameModule.Size.Height;

            // Apply drag delta based on which thumb was moved.
            if (sender == _bottomRight || sender == _right || sender == _topRight) newWidth += e.HorizontalChange;
            if (sender == _bottomRight || sender == _bottom || sender == _bottomLeft) newHeight += e.VerticalChange;
            if (sender == _topLeft || sender == _left || sender == _bottomLeft)
            {
                newLeft += e.HorizontalChange;
                newWidth -= e.HorizontalChange;
            }
            if (sender == _topLeft || sender == _top || sender == _topRight)
            {
                newTop += e.VerticalChange;
                newHeight -= e.VerticalChange;
            }

            // --- Snapping Logic ---
            _snapLineX = null;
            _snapLineY = null;

            // Check for snaps against other modules.
            foreach (var other in otherModules)
            {
                // Horizontal Snapping (Widths and Edges)
                if (sender == _right || sender == _bottomRight || sender == _topRight)
                {
                    if (System.Math.Abs(newWidth - other.Model.Size.Width) < SnapThreshold) { newWidth = other.Model.Size.Width; break; }
                    if (System.Math.Abs(newLeft + newWidth - other.Model.Position.X) < SnapThreshold) { newWidth = other.Model.Position.X - newLeft; _snapLineX = other.Model.Position.X; break; }
                    if (System.Math.Abs(newLeft + newWidth - (other.Model.Position.X + other.Model.Size.Width)) < SnapThreshold) { newWidth = other.Model.Position.X + other.Model.Size.Width - newLeft; _snapLineX = other.Model.Position.X + other.Model.Size.Width; break; }
                }
                if (sender == _left || sender == _bottomLeft || sender == _topLeft)
                {
                    if (System.Math.Abs(newWidth - other.Model.Size.Width) < SnapThreshold) { newLeft += newWidth - other.Model.Size.Width; newWidth = other.Model.Size.Width; break; }
                    if (System.Math.Abs(newLeft - other.Model.Position.X) < SnapThreshold) { double delta = newLeft - other.Model.Position.X; newLeft -= delta; newWidth += delta; _snapLineX = other.Model.Position.X; break; }
                    if (System.Math.Abs(newLeft - (other.Model.Position.X + other.Model.Size.Width)) < SnapThreshold) { double delta = newLeft - (other.Model.Position.X + other.Model.Size.Width); newLeft -= delta; newWidth += delta; _snapLineX = other.Model.Position.X + other.Model.Size.Width; break; }
                }

                // Vertical Snapping (Heights and Edges)
                if (sender == _bottom || sender == _bottomLeft || sender == _bottomRight)
                {
                    if (System.Math.Abs(newHeight - other.Model.Size.Height) < SnapThreshold) { newHeight = other.Model.Size.Height; break; }
                    if (System.Math.Abs(newTop + newHeight - other.Model.Position.Y) < SnapThreshold) { newHeight = other.Model.Position.Y - newTop; _snapLineY = other.Model.Position.Y; break; }
                    if (System.Math.Abs(newTop + newHeight - (other.Model.Position.Y + other.Model.Size.Height)) < SnapThreshold) { newHeight = other.Model.Position.Y + other.Model.Size.Height - newTop; _snapLineY = other.Model.Position.Y + other.Model.Size.Height; break; }
                }
                if (sender == _top || sender == _topLeft || sender == _topRight)
                {
                    if (System.Math.Abs(newHeight - other.Model.Size.Height) < SnapThreshold) { newTop += newHeight - other.Model.Size.Height; newHeight = other.Model.Size.Height; break; }
                    if (System.Math.Abs(newTop - other.Model.Position.Y) < SnapThreshold) { double delta = newTop - other.Model.Position.Y; newTop -= delta; newHeight += delta; _snapLineY = other.Model.Position.Y; break; }
                    if (System.Math.Abs(newTop - (other.Model.Position.Y + other.Model.Size.Height)) < SnapThreshold) { double delta = newTop - (other.Model.Position.Y + other.Model.Size.Height); newTop -= delta; newHeight += delta; _snapLineY = other.Model.Position.Y + other.Model.Size.Height; break; }
                }
            }

            // --- Enforce Minimum Size ---
            if (newWidth < MinModuleSize) { newLeft = gameModule.Position.X; newWidth = gameModule.Size.Width; }
            if (newHeight < MinModuleSize) { newTop = gameModule.Position.Y; newHeight = gameModule.Size.Height; }

            // --- Update the ViewModel ---
            gameModule.Size = new Size(newWidth, newHeight);
            gameModule.Position = new Point(newLeft, newTop);

            // Trigger a redraw to show/hide snap lines.
            this.InvalidateVisual();
        }

        #endregion
    }

    #endregion
}