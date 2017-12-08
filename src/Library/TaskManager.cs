using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class TaskManager : IDisposable, INotifyPropertyChanged
    {
        private readonly List<ITask> _tasks = new List<ITask>();
        private readonly List<TaskWatcher> _taskWatchers = new List<TaskWatcher>();

        private readonly Dictionary<string, WorkingLine> _workingLines = new Dictionary<string, WorkingLine>();
        public Dictionary<WorkingLine, bool> _workingLinesBusy = new Dictionary<WorkingLine, bool>();
        public ICollection<WorkingLine> WorkingLines => _workingLines.Values;

        private readonly ManualResetEvent _isRunningEvent = new ManualResetEvent(true);

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event PropertyChangedEventHandler PropertyChanged;

        #region Event Dispatch

        private readonly EventLoop _outerEventLoop = new EventLoop("External");

        private readonly EventLoop _innerEventLoop = new EventLoop("Internal");

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
                if (_isRunning) _isRunningEvent.Reset();

                if (_isRunning)
                {
                    TaskDebug.Verbose("TM: Started");
                    _outerEventLoop.FireEvent(this, Started);
                }
                OnIsRunningChanged(!_isRunning, _isRunning);
                if (!_isRunning)
                {
                    TaskDebug.Verbose("TM: Finished");
                    _outerEventLoop.FireEvent(this, Finished);
                }
            }
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<PropertyUpdateEventArgs<bool>> IsRunningChanged;

        private void OnIsRunningChanged(bool oldValue, bool newValue)
        {
            if (newValue == false)
            {
                try
                {
                    _isRunningEvent.Set();
                }
                catch (ObjectDisposedException) { }
            }
            _outerEventLoop.Push(IsRunningChanged, this,
                new PropertyUpdateEventArgs<bool>(nameof(IsRunning), oldValue, newValue));
            _outerEventLoop.Push(PropertyChanged, this,
                new PropertyChangedEventArgs(nameof(IsRunning)));
        }

        private void WorkingLineBusyChangedHandler(object sender, PropertyUpdateEventArgs<bool> e)
        {
            var workingLine = (WorkingLine)sender;
            var wlBusy = e.NewValue;
            var count = 0;
            _workingLinesBusy[workingLine] = wlBusy;
            foreach (var busy in _workingLinesBusy.Values)
            {
                if (busy) count++;
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
            foreach (var taskWatcher in _taskWatchers)
            {
                var taskState = taskWatcher.Task.State;
                if (taskState != TaskState.Succeeded && // technically not necessary
                    taskState != TaskState.Failed && // technically not necessary
                    taskState != TaskState.Obsolete &&
                    taskState != TaskState.Canceled)
                {
                    finished = false;
                    break;
                }
            }
            if (finished)
            {
                IsRunning = false;
            }
        }

        private int _busyWorkingLinesCount;
        public int BusyWorkingLinesCount
        {
            get => _busyWorkingLinesCount;
            private set
            {
                if (_busyWorkingLinesCount == value) return;
                var oldValue = _busyWorkingLinesCount;
                _busyWorkingLinesCount = value;
                OnBusyWorkingLinesCountChanged(oldValue, value);
            }
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<PropertyUpdateEventArgs<int>> BusyWorkingLinesCountChanged;

        private void OnBusyWorkingLinesCountChanged(int oldValue, int newValue)
        {
            _outerEventLoop.Push(BusyWorkingLinesCountChanged, this,
                new PropertyUpdateEventArgs<int>(nameof(BusyWorkingLinesCount), oldValue, newValue));
            _outerEventLoop.Push(PropertyChanged, this,
                new PropertyChangedEventArgs(nameof(BusyWorkingLinesCount)));
        }

        private bool _isCancelled;

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

        public int TaskCount => _tasks.Count;

        public void AddWorkingLine(string queueTag, IWorkerFactory factory, int worker, ThreadPriority threadPriority)
        {
            if (IsRunning) throw new InvalidOperationException("Working lines can not be added after the manager was started.");
            if (_workingLines.ContainsKey(queueTag)) throw new ArgumentException("The given tag is already in use for another working line.", nameof(queueTag));
            var workingLine = new WorkingLine(_innerEventLoop, queueTag, factory, worker, threadPriority);
            workingLine.BusyChanged += WorkingLineBusyChangedHandler;
            workingLine.TaskRejected += WorkingLineTaskRejectedHandler;
            workingLine.TaskBegin += WorkingLineTaskBeginHandler;
            workingLine.TaskEnd += WorkingLineTaskEndHandler;
            workingLine.WorkerError += WorkingLineWorkerErrorHandler;
            _workingLines[queueTag] = workingLine;
            _workingLinesBusy[workingLine] = workingLine.Busy;
        }

        #endregion

        #region Life Cycle

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TaskManager));
            if (IsRunning) throw new InvalidOperationException("The manager was already started.");
            _isCancelled = false;
            IsRunning = true;
            if (_tasks.Count == 0)
            {
                IsRunning = false;
                return;
            }

            _innerEventLoop.RunActionAsync(() =>
            {

                foreach (var workingLine in _workingLines.Values)
                {
                    workingLine.Start();
                }

                InitializeTaskWatchers();

                NotifyInitiallyReadyTasks();
            });
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler Started;

        public void Cancel() => _innerEventLoop.RunActionAsync(ProcessCancel);

        private void ProcessCancel()
        {
            _isCancelled = true;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Cancel();
            }
            _outerEventLoop.FireEvent(this, Canceled);
        }

        public event EventHandler Canceled;

        /// <remarks>
        /// Waiting for the end of the task manager activity does NOT mean, all queued events are fired.
        /// </remarks>
        public bool WaitForEnd(int timeout = Timeout.Infinite)
        {
            try
            {
                return _isRunningEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            };
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler Finished;

        private int _disposed = 0;
        public bool IsDisposed => _disposed != 0;

        public void Dispose()
        {
            var disposed = Interlocked.Exchange(ref _disposed, 1);
            if (disposed != 0) return;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Dispose();
            }
            _innerEventLoop.Dispose();
            _outerEventLoop.Dispose();
            _isRunningEvent.Close();
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
                var taskWatcher = new TaskWatcher(t, _innerEventLoop);
                taskWatcher.GotReady += TaskGotReadyHandler;
                taskWatcher.GotObsolete += TaskGotObsoleteHandler;
                _taskWatchers.Add(taskWatcher);
                TaskDebug.Verbose($"Watching task {t}");
            }
        }

        private void NotifyInitiallyReadyTasks()
        {
            foreach (var taskWatcher in _taskWatchers.ToArray())
            {
                if (taskWatcher.IsReady)
                {
                    TaskGotReadyHandler(taskWatcher, EventArgs.Empty);
                }
            }
        }

        private void RemoveTaskWatcher(TaskWatcher taskWatcher)
        {
            taskWatcher.GotReady -= TaskGotReadyHandler;
            taskWatcher.GotObsolete -= TaskGotObsoleteHandler;
            _taskWatchers.Remove(taskWatcher);
        }

        private void TaskGotReadyHandler(object sender, EventArgs e)
        {
            var taskWatcher = (TaskWatcher)sender;

            System.Diagnostics.Debug.Assert(taskWatcher.IsReady,
                "A task watcher was reported as ready, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} got ready");

            if (!_isCancelled)
            {
                DispatchTask(taskWatcher.Task);
            }
            else
            {
                taskWatcher.Task.UpdateState(TaskState.Obsolete);
            }
        }

        private void TaskGotObsoleteHandler(object sender, EventArgs e)
        {
            var taskWatcher = (TaskWatcher)sender;

            System.Diagnostics.Debug.Assert(taskWatcher.IsObsolete,
                "A task watcher was reported as obsolete, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} got obsolete");
            RemoveTaskWatcher(taskWatcher);
        }

        private void TaskFinishedHandler(object sender, EventArgs e)
        {
            var taskWatcher = (TaskWatcher)sender;

            System.Diagnostics.Debug.Assert(taskWatcher.IsFinished,
                "A task watcher was reported as finished, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} finished");
            RemoveTaskWatcher(taskWatcher);
        }

        private void DispatchTask(ITask task)
        {
            TaskDebug.Verbose($"TM: Dispatching task {task}");
            var workingLine = _workingLines[task.QueueTag];
            workingLine.Enqueue(task);
        }

        private void WorkingLineTaskRejectedHandler(object sender, TaskRejectedEventArgs e)
            =>_outerEventLoop.FireEvent(this, TaskRejected, e);

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskRejectedEventArgs> TaskRejected;

        private void WorkingLineTaskBeginHandler(object sender, TaskEventArgs e)
            => _outerEventLoop.FireEvent(this, TaskBegin, e);

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskEventArgs> TaskBegin;

        private void WorkingLineTaskEndHandler(object sender, TaskEventArgs e)
            => _outerEventLoop.FireEvent(this, TaskEnd, e);

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskEventArgs> TaskEnd;

        private void WorkingLineWorkerErrorHandler(object sender, WorkerErrorEventArgs e)
            => _outerEventLoop.FireEvent(this, WorkerError, e);

        public event EventHandler<WorkerErrorEventArgs> WorkerError;

        // in case the workerlines finish thier work, the state of the remaining tasks must be checked for waiting tasks
        // if all remaining tasks are failed, obsolete or canceled, the manager has finished; otherwise waiting tasks are getting ready

        #endregion
    }
}
