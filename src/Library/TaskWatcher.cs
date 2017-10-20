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
            task.StateChanged += TaskStateChangedHandler;
            IsObsolete = task.State != TaskState.Obsolete;
        }

        private void DependencyStateChangedHandler(object sender, PropertyUpdateEventArgs<TaskState> e)
        {
            var dep = (ITask)sender;
            var depState = e.NewValue;
            if (depState != TaskState.Succeeded && 
                depState != TaskState.Failed &&
                depState != TaskState.Canceled &&
                depState != TaskState.Obsolete)
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
                        notify = true;
                    }
                }
            }
            if (depState == TaskState.Succeeded)
            {
                if (notify)
                {
                    TaskDebug.Verbose($"TW: Observed {dep} succeed -> {Task} got ready");
                    IsReady = true;
                    GotReady?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                TaskDebug.Verbose($"TW: Observed {dep} get {depState} -> {Task} got obsolete");
                lock (_incompleteDependencies)
                {
                    foreach (var d in _incompleteDependencies)
                    {
                        d.StateChanged -= DependencyStateChangedHandler;
                    }
                    _incompleteDependencies.Clear();
                }
                Task.UpdateState(TaskState.Obsolete, 
                    depState == TaskState.Failed ? new DependencyFailedException(dep) : null);
            } 
        }

        private void TaskStateChangedHandler(object sender, PropertyUpdateEventArgs<TaskState> e)
        {
            if (e.NewValue == TaskState.Obsolete)
            {
                IsObsolete = true;
                TaskDebug.Verbose($"TW: Observed {Task} get obsolete");
                GotObsolete?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsReady { get; private set; }

        public event EventHandler GotReady;

        public bool IsObsolete { get; private set; }

        public event EventHandler GotObsolete;
    }
}
