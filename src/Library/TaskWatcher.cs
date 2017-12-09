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
            task.ProgressChanged += TaskProgressChangedHandler;
            task.ProgressMessageChanged += TaskProgressMessageChangeHandler;
            IsObsolete = task.State != TaskState.Obsolete;
            HasEnded = task.State == TaskState.Succeeded ||
                         task.State == TaskState.Failed ||
                         Task.State == TaskState.Canceled;
        }

        private void DependencyStateChangedHandler(object sender, PropertyUpdateEventArgs<TaskState> e)
            => _eventLoop.RunActionAsync(() => ProcessDependencyStateChange((ITask)sender, e.OldValue, e.NewValue));

        private void TaskStateChangedHandler(object sender, PropertyUpdateEventArgs<TaskState> e)
            => _eventLoop.RunActionAsync(() => ProcessTaskStateChange(e.OldValue, e.NewValue));

        private void TaskProgressChangedHandler(object sender, PropertyUpdateEventArgs<float> e)
            => _eventLoop.RunActionAsync(() => ProcessTaskProgressChange(e));

        private void TaskProgressMessageChangeHandler(object sender, PropertyUpdateEventArgs<string> e)
            => _eventLoop.RunActionAsync(() => ProcessTaskProgressMessageChange(e));

        /// <remarks>Runs on the event loop.</remarks>
        private void ProcessDependencyStateChange(ITask dep, TaskState oldState, TaskState newState)
        {
            _eventLoop.AssertThread();

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
                    _eventLoop.FireEvent(this, GotReady, new TaskEventArgs(Task, TaskState.Waiting));
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

        /// <remarks>Runs on the event loop.</remarks>
        private void ProcessTaskStateChange(TaskState oldValue, TaskState newValue)
        {
            _eventLoop.AssertThread();

            if (newValue == TaskState.InProgress)
            {
                HasBegun = true;
                TaskDebug.Verbose($"TW: Observed {Task} starting");
                _eventLoop.FireEvent(this, Begin, new TaskEventArgs(Task, newValue));
            }
            else if (newValue == TaskState.Obsolete)
            {
                IsObsolete = true;
                TaskDebug.Verbose($"TW: Observed {Task} get obsolete");
                _eventLoop.FireEvent(this, GotObsolete, new TaskEventArgs(Task, newValue));
            }
            else if (newValue == TaskState.Succeeded
                || newValue == TaskState.Failed
                || newValue == TaskState.Canceled)
            {
                HasEnded = true;
                TaskDebug.Verbose($"TW: Observed {Task} finished as {newValue}");
                _eventLoop.FireEvent(this, End, new TaskEventArgs(Task, newValue));
            }
        }

        /// <remarks>Runs on the event loop.</remarks>
        private void ProcessTaskProgressChange(PropertyUpdateEventArgs<float> e)
        {
            _eventLoop.AssertThread();

            Progress = e.NewValue;
            _eventLoop.FireEvent(this, ProgressChanged, 
                new TaskPropertyUpdateEventArgs<float>(Task, nameof(ITask.Progress), e.OldValue, e.NewValue));
        }

        /// <remarks>Runs on the event loop.</remarks>
        private void ProcessTaskProgressMessageChange(PropertyUpdateEventArgs<string> e)
        {
            _eventLoop.AssertThread();

            ProgressMessage = e.NewValue;
            _eventLoop.FireEvent(this, ProgressMessageChanged,
                new TaskPropertyUpdateEventArgs<string>(Task, nameof(ITask.ProgressMessage), e.OldValue, e.NewValue));
        }

        #region Dependency Events

        public bool IsReady { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler<TaskEventArgs> GotReady;

        public bool IsObsolete { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler<TaskEventArgs> GotObsolete;

        #endregion

        #region Task Events

        public bool HasBegun { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler<TaskEventArgs> Begin;

        public bool HasEnded { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler<TaskEventArgs> End;

        public float Progress { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler<TaskPropertyUpdateEventArgs<float>> ProgressChanged;

        public string ProgressMessage { get; private set; }

        /// <remarks>Is firing on the event loop.</remarks>
        public event EventHandler<TaskPropertyUpdateEventArgs<string>> ProgressMessageChanged;

        #endregion
    }
}
