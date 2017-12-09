using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class TaskPropertyUpdateEventArgs<T> : EventArgs
    {
        public ITask Task { get; private set; }

        public string PropertyName { get; private set; }

        public T OldValue { get; private set; }

        public T NewValue { get; private set; }

        public TaskPropertyUpdateEventArgs(ITask task, string propertyName, T oldValue, T newValue)
        {
            Task = task;
            PropertyName = propertyName;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}
