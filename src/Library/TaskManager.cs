using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class TaskManager : IDisposable, INotifyPropertyChanged
    {
        private readonly List<ITask> _tasks = new List<ITask>();
        private readonly List<TaskWatcher> _taskWatchers = new List<TaskWatcher>();
        private readonly Dictionary<string, WorkingLine> _workingLines = new Dictionary<string, WorkingLine>();

        private readonly ManualResetEvent _busyEvent = new ManualResetEvent(true);

        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnIsRunningChanged();

                if (_isRunning)
                    _busyEvent.Reset();
                else
                    _busyEvent.Set();
            }
        }

        private bool _isWaitingForTheEnd = false;

        public event EventHandler IsRunningChanged;

        private void OnIsRunningChanged()
        {
            IsRunningChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }

        public void AddTask(ITask task)
        {
            if (IsRunning) throw new InvalidOperationException("Tasks can not be added while the manager is running.");
            _tasks.Add(task);
        }

        public void AddTasks(IEnumerable<ITask> tasks)
        {
            foreach (var task in tasks) AddTask(task);
        }

        public void ClearTasks()
        {
            if (IsRunning) throw new InvalidOperationException("Tasks can not be removed while the manager is running.");
            _tasks.Clear();
        }

        public void AddWorkingLine(string tag, IWorkerFactory factory, int worker, ThreadPriority threadPriority)
        {
            if (IsRunning) throw new InvalidOperationException("Working lines can not be added after the manager was started.");
            if (_workingLines.ContainsKey(tag)) throw new ArgumentException("The given tag is already in use for another working line.", nameof(tag));
            var workingLine = new WorkingLine(tag, factory, worker, threadPriority);
            workingLine.BusyChanged += WorkingLineBusyChanged;

            _workingLines[tag] = workingLine;
        }

        private readonly object _workingLineBusyCountLock = new object();

        private void WorkingLineBusyChanged(object sender, EventArgs e)
        {
            var count = 0;
            lock(_workingLineBusyCountLock)
            {
                foreach (var workingLine in _workingLines.Values)
                {
                    if (workingLine.Busy) count++;
                }
            }
            BusyWorkingLinesCount = count;
        }

        private int _busyWorkingLinesCount;
        public int BusyWorkingLinesCount
        {
            get => _busyWorkingLinesCount;
            set
            {
                if (_busyWorkingLinesCount == value) return;
                _busyWorkingLinesCount = value;
                OnBusyWorkingLinesCountChanged();
            }
        }

        public event EventHandler BusyWorkingLinesCountChanged;

        private void OnBusyWorkingLinesCountChanged()
        {
            BusyWorkingLinesCountChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BusyWorkingLinesCount)));
        }

        public ICollection<WorkingLine> WorkingLines => _workingLines.Values;

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TaskManager));
            if (IsRunning) throw new InvalidOperationException("The manager was already started.");

            _isWaitingForTheEnd = false;
            IsRunning = true;
            if (_tasks.Count == 0)
            {
                IsRunning = false;
                return;
            }

            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Start();
            }

            _taskWatchers.Clear();
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
            lock (_taskWatchers)
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
            _busyEvent.WaitOne();
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
