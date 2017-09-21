using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Mastersign.Tasks
{
    /// <summary>
    /// This event argument class is used in decoupled event scenarios,
    /// where the event is fired on a different thread then the firing object operates on.
    /// </summary>
    /// <seealso cref="TaskManager"/>
    public abstract class PropertyUpdateEventArgs : PropertyChangedEventArgs
    {
        protected PropertyUpdateEventArgs(string propertyName)
            : base(propertyName)
        {
        }

        public abstract object GetOldValue();

        public abstract object GetNewValue();
    }

    public class PropertyUpdateEventArgs<T> : PropertyUpdateEventArgs
    {
        public T OldValue { get; }
        public T NewValue { get; }

        public PropertyUpdateEventArgs(string propertyName, T oldValue, T newValue)
            : base(propertyName)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }

        public override object GetOldValue() => OldValue;

        public override object GetNewValue() => NewValue;
    }
}
