using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class TaskManager : IDisposable
    {
        private readonly List<ITask> _tasks = new List<ITask>();
        private readonly List<TaskWatcher> _taskWatchers = new List<TaskWatcher>();
        private readonly Dictionary<string, WorkingLine> _workingLines = new Dictionary<string, WorkingLine>();

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnIsRunningChanged();
            }
        }

        private bool _isWaitingForTheEnd = false;

        public event EventHandler IsRunningChanged;

        private void OnIsRunningChanged()
        {
            IsRunningChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddTask(ITask task)
        {
            if (_isRunning) throw new InvalidOperationException("Tasks can not be added after the manager was started.");
            _tasks.Add(task);
        }

        public void AddTasks(IEnumerable<ITask> tasks)
        {
            foreach (var task in tasks) AddTask(task);
        }

        public void AddWorkingLine(string tag, IWorkerFactory factory, int worker, ThreadPriority threadPriority)
        {
            if (_isRunning) throw new InvalidOperationException("Working lines can not be added after the manager was started.");
            if (_workingLines.ContainsKey(tag)) throw new ArgumentException("The given tag is already in use for another working line.", nameof(tag));
            var workingLine = new WorkingLine(tag, factory, worker, threadPriority);
            _workingLines[tag] = workingLine;
        }

        public ICollection<WorkingLine> WorkingLines => _workingLines.Values;

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TaskManager));
            if (_isRunning) throw new InvalidOperationException("The manager was already started.");
            _isRunning = true;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Start();
            }

            foreach (var t in _tasks)
            {
                if (!_workingLines.ContainsKey(t.QueueTag))
                {
                    throw new InvalidOperationException(
                        $"A task has the queue tag '{t.QueueTag}', but for this tag exists no working line.");
                }
                var taskWatcher = new TaskWatcher(t);
                taskWatcher.IsReadyChanged += TaskIsReadyChangedHandler;
                _taskWatchers.Add(taskWatcher);
            }

            foreach (var taskWatcher in _taskWatchers.ToArray())
            {
                if (taskWatcher.IsReady)
                {
                    taskWatcher.IsReadyChanged -= TaskIsReadyChangedHandler;
                    TaskIsReadyChangedHandler(taskWatcher, EventArgs.Empty);
                }
            }
        }

        private void TaskIsReadyChangedHandler(object sender, EventArgs e)
        {
            var taskWatcher = (TaskWatcher)sender;

            Debug.Assert(taskWatcher.IsReady, 
                "A task watcher was reported as ready, but it is not.");
            Debug.Assert(taskWatcher.Task.State == TaskState.Waiting,
                "A task watcher reported a non waiting task as ready.");

            taskWatcher.IsReadyChanged -= TaskIsReadyChangedHandler;
            lock(_taskWatchers)
            {
                _taskWatchers.Remove(taskWatcher);
                if (_taskWatchers.Count == 0)
                {
                    _isWaitingForTheEnd = true;
                }
            }
            DispatchTask(taskWatcher.Task);
        }

        private void DispatchTask(ITask task)
        {
            var workingLine = _workingLines[task.QueueTag];
            workingLine.Enqueue(task);
        }

        // in case the workerlines finish thier work, the state of the remaining tasks must be checked for waiting tasks
        // if all remaining tasks are failed, obsolete or canceled, the manager has finished; otherwise waiting tasks are getting ready

        public void Cancel()
        {
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Cancel();
            }
            _isWaitingForTheEnd = true;
        }

        public void WaitForEnd()
        {
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.WaitForEnd();
            }
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Dispose();
            }
        }
    }
}
