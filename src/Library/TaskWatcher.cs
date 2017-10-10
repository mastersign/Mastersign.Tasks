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
            _incompleteDependencies.AddRange(task.Dependencies);
            foreach (var dep in _incompleteDependencies)
            {
                dep.StateChanged += DependencyStateChangedHandler;
            }
            IsReady = _incompleteDependencies.Count == 0;
        }

        private void DependencyStateChangedHandler(object sender, PropertyUpdateEventArgs<TaskState> e)
        {
            var dep = (ITask)sender;
            var depState = e.NewValue;
            if (depState != TaskState.Succeeded && 
                depState != TaskState.Failed)
            {
                return;
            }
            var notify = false;
            lock (_incompleteDependencies)
            {
                if (_incompleteDependencies.Contains(dep))
                {
                    _incompleteDependencies.Remove(dep);
                    dep.StateChanged -= DependencyStateChangedHandler;
                    if (_incompleteDependencies.Count == 0)
                    {
                        IsReady = true;
                        notify = true;
                    }
                }
            }
            if (depState == TaskState.Failed)
            {
                Task.UpdateState(TaskState.Obsolete, new DependencyFailedException(dep));
            }
            else if (depState == TaskState.Succeeded && notify)
            {
                IsReadyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsReady { get; private set; }

        public event EventHandler IsReadyChanged;
    }
}
