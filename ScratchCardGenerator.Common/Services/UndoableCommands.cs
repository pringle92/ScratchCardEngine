#region Usings

using System;
using System.Collections.ObjectModel;
using System.Reflection;

#endregion

namespace ScratchCardGenerator.Common.Services
{
    #region AddItemCommand

    /// <summary>
    /// Represents the action of adding an item to a collection as an undoable command.
    /// This class encapsulates all information required to perform and undo the addition.
    /// </summary>
    /// <typeparam name="T">The type of the item in the collection.</typeparam>
    public class AddItemCommand<T> : IUndoableCommand
    {
        #region Private Fields

        /// <summary>
        /// A reference to the target collection that the item will be added to.
        /// </summary>
        private readonly ObservableCollection<T> _collection;

        /// <summary>
        /// A reference to the item being added.
        /// </summary>
        private readonly T _item;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="AddItemCommand{T}"/> class.
        /// </summary>
        /// <param name="collection">The collection to be modified.</param>
        /// <param name="item">The item to be added.</param>
        public AddItemCommand(ObservableCollection<T> collection, T item)
        {
            _collection = collection;
            _item = item;
        }

        #endregion

        #region IUndoableCommand Implementation

        /// <summary>
        /// Executes the command by adding the item to the collection.
        /// </summary>
        public void Execute() => _collection.Add(_item);

        /// <summary>
        /// Undoes the command by removing the item from the collection.
        /// </summary>
        public void Undo() => _collection.Remove(_item);

        #endregion
    }

    #endregion

    #region RemoveItemCommand

    /// <summary>
    /// Represents the action of removing an item from a collection as an undoable command.
    /// It stores the item's original index to ensure a perfect reversal.
    /// </summary>
    /// <typeparam name="T">The type of the item in the collection.</typeparam>
    public class RemoveItemCommand<T> : IUndoableCommand
    {
        #region Private Fields

        private readonly ObservableCollection<T> _collection;
        private readonly T _item;

        /// <summary>
        /// Stores the original index of the item before it was removed. This is critical for the Undo operation.
        /// </summary>
        private int _index;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="RemoveItemCommand{T}"/> class.
        /// </summary>
        /// <param name="collection">The collection to be modified.</param>
        /// <param name="item">The item to be removed.</param>
        public RemoveItemCommand(ObservableCollection<T> collection, T item)
        {
            _collection = collection;
            _item = item;
        }

        #endregion

        #region IUndoableCommand Implementation

        /// <summary>
        /// Executes the command by finding the item's index and then removing it from the collection.
        /// </summary>
        public void Execute()
        {
            // We must capture the index *before* removing the item.
            _index = _collection.IndexOf(_item);
            _collection.Remove(_item);
        }

        /// <summary>
        /// Undoes the command by inserting the item back into the collection at its original index.
        /// Using Insert instead of Add ensures the original order is perfectly restored.
        /// </summary>
        public void Undo() => _collection.Insert(_index, _item);

        #endregion
    }

    #endregion

    #region UpdatePropertyCommand

    /// <summary>
    /// Represents the action of changing a property on an object as an undoable command.
    /// This is a highly reusable command that can be used for any property change (e.g., resizing, moving, renaming).
    /// </summary>
    public class UpdatePropertyCommand : IUndoableCommand
    {
        #region Private Fields

        private readonly object _target;
        private readonly PropertyInfo _propertyInfo;
        private readonly object _oldValue;
        private readonly object _newValue;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="UpdatePropertyCommand"/> class.
        /// </summary>
        /// <param name="target">The object whose property is being changed.</param>
        /// <param name="propertyName">The name of the property to change.</param>
        /// <param name="newValue">The new value for the property.</param>
        public UpdatePropertyCommand(object target, string propertyName, object newValue)
        {
            _target = target;

            // Use reflection to get a reference to the property itself.
            _propertyInfo = target.GetType().GetProperty(propertyName);

            // Capture the state *before* the change.
            _oldValue = _propertyInfo.GetValue(target);
            _newValue = newValue;
        }

        /// <summary>
        /// NEW: An alternative constructor that accepts the old value directly.
        /// This is useful in scenarios where the old value is captured by an event before the property is updated.
        /// </summary>
        public UpdatePropertyCommand(object target, string propertyName, object oldValue, object newValue)
        {
            _target = target;
            _propertyInfo = target.GetType().GetProperty(propertyName);
            _oldValue = oldValue; // Use the provided old value
            _newValue = newValue;
        }

        #endregion

        #region IUndoableCommand Implementation

        /// <summary>
        /// Executes the command by setting the property to the new value.
        /// </summary>
        public void Execute() => _propertyInfo.SetValue(_target, _newValue);

        /// <summary>
        /// Undoes the command by setting the property back to its original old value.
        /// </summary>
        public void Undo() => _propertyInfo.SetValue(_target, _oldValue);

        #endregion
    }

    #endregion

    #region MoveItemCommand

    /// <summary>
    /// Represents the action of moving an item within a collection as an undoable command.
    /// </summary>
    /// <typeparam name="T">The type of the item in the collection.</typeparam>
    public class MoveItemCommand<T> : IUndoableCommand
    {
        #region Private Fields

        private readonly ObservableCollection<T> _collection;
        private readonly T _item;
        private readonly int _direction;
        private int _oldIndex;
        private int _newIndex;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialises a new instance of the <see cref="MoveItemCommand{T}"/> class.
        /// </summary>
        /// <param name="collection">The collection to be modified.</param>
        /// <param name="item">The item to be moved.</param>
        /// <param name="direction">The direction to move the item (-1 for up, 1 for down).</param>
        public MoveItemCommand(ObservableCollection<T> collection, T item, int direction)
        {
            _collection = collection;
            _item = item;
            _direction = direction;
        }

        #endregion

        #region IUndoableCommand Implementation

        /// <summary>
        /// Executes the command by calculating the new index and moving the item.
        /// </summary>
        public void Execute()
        {
            _oldIndex = _collection.IndexOf(_item);
            _newIndex = _oldIndex + _direction;

            // Boundary check to ensure the move is valid.
            if (_newIndex >= 0 && _newIndex < _collection.Count)
            {
                _collection.Move(_oldIndex, _newIndex);
            }
        }

        /// <summary>
        /// Undoes the command by moving the item back to its original index.
        /// </summary>
        public void Undo()
        {
            // The indices are swapped for the undo operation.
            if (_newIndex >= 0 && _newIndex < _collection.Count)
            {
                _collection.Move(_newIndex, _oldIndex);
            }
        }

        #endregion
    }

    #endregion
}