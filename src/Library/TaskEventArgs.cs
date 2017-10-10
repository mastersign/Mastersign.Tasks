using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class TaskEventArgs : EventArgs
    {
        public ITask Task { get; private set; }

        public TaskState State { get; private set; }

        public TaskEventArgs(ITask task)
        {
            Task = task;
            State = task.State;
        }

        public TaskEventArgs(ITask task, TaskState state)
        {
            Task = task;
            State = state;
        }
    }
}
