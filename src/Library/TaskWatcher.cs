using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    internal class TaskWatcher
    {
        private readonly EventLoop _eventLoop;
        public ITask Task { get; private set; }

        private List<ITask> _incompleteDependencies = new List<ITask>();

        public TaskWatcher(ITask task, EventLoop eventLoop)
        {
            Task = task;
            _eventLoop = eventLoop;
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
            => _eventLoop.RunActionAsync(() => ProcessDependencyStateChange((ITask)sender, e.OldValue, e.NewValue));

        private void TaskStateChangedHandler(object sender, PropertyUpdateEventArgs<TaskState> e)
            => _eventLoop.RunActionAsync(() => ProcessTaskStateChange(e.OldValue, e.NewValue));

        private void ProcessDependencyStateChange(ITask dep, TaskState oldState, TaskState newState)
        {
            var depState = newState;
            if (depState != TaskState.Succeeded &&
                depState != TaskState.Failed &&
                depState != TaskState.Canceled &&
                depState != TaskState.Obsolete)
            {
                return;
            }
            var notify = false;
            if (_incompleteDependencies.Contains(dep))
            {
                _incompleteDependencies.Remove(dep);
                dep.StateChanged -= DependencyStateChangedHandler;
                if (_incompleteDependencies.Count == 0)
                {
                    notify = true;
                }
            }
            if (depState == TaskState.Succeeded)
            {
                if (notify)
                {
                    TaskDebug.Verbose($"TW: Observed {dep} succeed -> {Task} got ready");
                    IsReady = true;
                    _eventLoop.FireEvent(this, GotReady);
                }
            }
            else
            {
                TaskDebug.Verbose($"TW: Observed {dep} get {depState} -> {Task} got obsolete");
                foreach (var d in _incompleteDependencies)
                {
                    d.StateChanged -= DependencyStateChangedHandler;
                }
                _incompleteDependencies.Clear();
                Task.UpdateState(TaskState.Obsolete,
                    depState == TaskState.Failed ? new DependencyFailedException(dep) : null);
            }
        }

        private void ProcessTaskStateChange(TaskState oldValue, TaskState newValue)
        {
            if (newValue == TaskState.Obsolete)
            {
                IsObsolete = true;
                TaskDebug.Verbose($"TW: Observed {Task} get obsolete");
                _eventLoop.FireEvent(this, GotObsolete);
            }
        }

        public bool IsReady { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler GotReady;

        public bool IsObsolete { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler GotObsolete;
    }
}
