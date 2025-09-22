#region Usings

using ScratchCardGenerator.Common.Services;
using System.Windows;
using System.Windows.Controls;

#endregion

namespace ScratchCardGenerator.Common.Behaviours
{
    /// <summary>
    /// An attached behavior that adds Undo/Redo support to a TextBox.
    /// It works by capturing the text value on GotFocus and creating an UpdatePropertyCommand on LostFocus if the value has changed.
    /// </summary>
    public static class UndoableTextBoxBehavior
    {
        private static string _originalValue = null;

        public static readonly DependencyProperty UndoRedoServiceProperty =
            DependencyProperty.RegisterAttached("UndoRedoService", typeof(UndoRedoService), typeof(UndoableTextBoxBehavior), new PropertyMetadata(null, OnUndoRedoServiceChanged));

        public static UndoRedoService GetUndoRedoService(DependencyObject obj) => (UndoRedoService)obj.GetValue(UndoRedoServiceProperty);
        public static void SetUndoRedoService(DependencyObject obj, UndoRedoService value) => obj.SetValue(UndoRedoServiceProperty, value);

        private static void OnUndoRedoServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                textBox.GotFocus -= TextBox_GotFocus;
                textBox.LostFocus -= TextBox_LostFocus;

                if (e.NewValue is UndoRedoService)
                {
                    textBox.GotFocus += TextBox_GotFocus;
                    textBox.LostFocus += TextBox_LostFocus;
                }
            }
        }

        private static void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _originalValue = textBox.Text;
            }
        }

        private static void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (_originalValue != null && _originalValue != textBox.Text)
                {
                    var undoRedoService = GetUndoRedoService(textBox);
                    var dataContext = textBox.DataContext;
                    var binding = textBox.GetBindingExpression(TextBox.TextProperty);

                    if (undoRedoService != null && dataContext != null && binding != null)
                    {
                        var command = new UpdatePropertyCommand(dataContext, binding.ResolvedSourcePropertyName, _originalValue, textBox.Text);
                        undoRedoService.ExecuteCommand(command);
                    }
                }
                _originalValue = null;
            }
        }
    }
}