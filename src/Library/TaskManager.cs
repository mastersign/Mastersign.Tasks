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
        public ICollection<WorkingLine> WorkingLines => _workingLines.Values;

        private readonly ManualResetEvent _isRunningEvent = new ManualResetEvent(true);

        public event PropertyChangedEventHandler PropertyChanged;

        #region Event Dispatch

        private readonly EventLoop _eventLoop = new EventLoop();

        private void Notify(ActionHandle action)
        {
            _eventLoop.Push(action);
        }

        private void Notify(EventHandler handler)
        {
            _eventLoop.Push(handler, this, EventArgs.Empty);
        }

        private void Notify<T>(EventHandler<T> handler, T e) where T : EventArgs
        {
            _eventLoop.Push(handler, this, e);
        }

        #endregion

        #region State

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;

                if (_isRunning)
                    _isRunningEvent.Reset();
                else
                    _isRunningEvent.Set();

                Notify(OnIsRunningChanged);
            }
        }

        public event EventHandler IsRunningChanged;

        private void OnIsRunningChanged()
        {
            IsRunningChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }

        private readonly object _workingLineBusyCountLock = new object();

        private void WorkingLineBusyChangedHandler(object sender, EventArgs e)
        {
            var count = 0;
            lock (_workingLineBusyCountLock)
            {
                foreach (var workingLine in _workingLines.Values)
                {
                    if (workingLine.Busy) count++;
                }
            }
            BusyWorkingLinesCount = count;
            if (count == 0)
            {
                CheckForEnd();
            }
        }

        private void CheckForEnd()
        {
            var finished = true;
            lock (_taskWatchers)
            {
                foreach (var taskWatcher in _taskWatchers)
                {
                    var taskState = taskWatcher.Task.State;
                    if (taskState != TaskState.Obsolete && taskState != TaskState.Canceled)
                    {
                        finished = false;
                        break;
                    }
                }
            }
            if (finished)
            {
                IsRunning = false;
                Notify(Finished);
            }
        }

        private int _busyWorkingLinesCount;
        public int BusyWorkingLinesCount
        {
            get => _busyWorkingLinesCount;
            set
            {
                if (_busyWorkingLinesCount == value) return;
                _busyWorkingLinesCount = value;
                Notify(OnBusyWorkingLinesCountChanged);
            }
        }

        public event EventHandler BusyWorkingLinesCountChanged;

        private void OnBusyWorkingLinesCountChanged()
        {
            BusyWorkingLinesCountChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BusyWorkingLinesCount)));
        }

        #endregion

        #region Initialization

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
            workingLine.BusyChanged += WorkingLineBusyChangedHandler;
            workingLine.TaskRejected += WorkingLineTaskRejectedHandler;
            workingLine.TaskBegin += WorkingLineTaskBeginHandler;
            workingLine.TaskEnd += WorkingLineTaskEndHandler;
            workingLine.WorkerError += WorkingLineWorkerErrorHandler;
            _workingLines[tag] = workingLine;
        }

        #endregion

        #region Life Cycle

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TaskManager));
            if (IsRunning) throw new InvalidOperationException("The manager was already started.");

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

            InitializeTaskWatchers();

            Notify(Started);

            NotifyInitiallyReadyTasks();
        }

        public event EventHandler Started;

        public void Cancel()
        {
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Cancel();
            }
            Notify(Canceled);
        }

        public event EventHandler Canceled;

        public void WaitForEnd()
        {
            _isRunningEvent.WaitOne();
        }

        public event EventHandler Finished;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Dispose();
            }
            _eventLoop.Dispose();
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Disposed;

        #endregion

        #region Task Management

        private void InitializeTaskWatchers()
        {
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
        }

        private void NotifyInitiallyReadyTasks()
        {
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
            }
            DispatchTask(taskWatcher.Task);
        }

        private void DispatchTask(ITask task)
        {
            var workingLine = _workingLines[task.QueueTag];
            workingLine.Enqueue(task);
        }

        private void WorkingLineTaskRejectedHandler(object sender, TaskRejectedEventArgs e)
        {
            Notify(TaskRejected, e);
        }

        public event EventHandler<TaskRejectedEventArgs> TaskRejected;

        private void WorkingLineTaskBeginHandler(object sender, TaskEventArgs e)
        {
            Notify(TaskBegin, e);
        }

        public event EventHandler<TaskEventArgs> TaskBegin;

        private void WorkingLineTaskEndHandler(object sender, TaskEventArgs e)
        {
            Notify(TaskEnd, e);
        }

        public event EventHandler<TaskEventArgs> TaskEnd;

        private void WorkingLineWorkerErrorHandler(object sender, WorkerErrorEventArgs e)
        {
            Notify(WorkerError, e);
        }

        public event EventHandler<WorkerErrorEventArgs> WorkerError;

        // in case the workerlines finish thier work, the state of the remaining tasks must be checked for waiting tasks
        // if all remaining tasks are failed, obsolete or canceled, the manager has finished; otherwise waiting tasks are getting ready

        #endregion
    }
}
