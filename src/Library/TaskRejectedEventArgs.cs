using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class TaskRejectedEventArgs : EventArgs
    {
        public TaskRejectedEventArgs(ITask task, TaskState rejectedState)
        {
            Task = task ?? throw new ArgumentNullException();
            RejectedState = rejectedState;
        }

        public ITask Task { get; private set; }

        public TaskState RejectedState { get; private set; }
    }
}
