using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    internal class TaskWatcher
    {
        public ITask Task { get; private set; }

        private List<ITask> _incompleteDependencies = new List<ITask>();

        public TaskWatcher(ITask task)
        {
            Task = task;
            task.StateChanged += TaskStateChangedHandler;
        }

        private void TaskStateChangedHandler(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
