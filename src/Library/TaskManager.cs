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

        private delegate void PropertyChangedNotifier<T>(T oldValue, T newValue);

        private void Notify<T>(PropertyChangedNotifier<T> handler, T oldValue, T newValue)
        {
            _eventLoop.Push(handler, oldValue, newValue);
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
                if (_isRunning) _isRunningEvent.Reset();

                if (_isRunning)
                {
                    TaskDebug.Verbose("TM: Started");
                    Notify(Started);
                }
                Notify(OnIsRunningChanged, !_isRunning, _isRunning);
                if (!_isRunning)
                {
                    TaskDebug.Verbose("TM: Finished");
                    Notify(Finished);
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
            try
            {
                IsRunningChanged?.Invoke(this,
                    new PropertyUpdateEventArgs<bool>(nameof(IsRunning), oldValue, newValue));
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(IsRunning)));
            }
            finally
            {
                if (newValue == false)
                {
                    try
                    {
                        _isRunningEvent.Set();
                    }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private void WorkingLineBusyChangedHandler(object sender, PropertyUpdateEventArgs<bool> e)
        {
            var workingLine = (WorkingLine)sender;
            var wlBusy = e.NewValue;
            var count = 0;
            lock (_workingLinesBusy)
            {
                _workingLinesBusy[workingLine] = wlBusy;
                foreach (var busy in _workingLinesBusy.Values)
                {
                    if (busy) count++;
                }
                BusyWorkingLinesCount = count;
            }
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
                Notify(OnBusyWorkingLinesCountChanged, oldValue, value);
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
            BusyWorkingLinesCountChanged?.Invoke(this,
                new PropertyUpdateEventArgs<int>(nameof(BusyWorkingLinesCount), oldValue, newValue));
            PropertyChanged?.Invoke(this,
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
            var workingLine = new WorkingLine(queueTag, factory, worker, threadPriority);
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

            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Start();
            }

            InitializeTaskWatchers();

            NotifyInitiallyReadyTasks();
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler Started;

        public void Cancel()
        {
            _isCancelled = true;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Cancel();
            }
            Notify(Canceled);
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

        public bool IsDisposed { get; private set; }
        private readonly object _disposeLock = new object();

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;
            }
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Dispose();
            }
            _eventLoop.Dispose();
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
                var taskWatcher = new TaskWatcher(t);
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
            lock (_taskWatchers)
            {
                _taskWatchers.Remove(taskWatcher);
            }
        }

        private void TaskGotReadyHandler(object sender, EventArgs e)
        {
            var taskWatcher = (TaskWatcher)sender;

            System.Diagnostics.Debug.Assert(taskWatcher.IsReady,
                "A task watcher was reported as ready, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} got ready");
            RemoveTaskWatcher(taskWatcher);

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

        private void DispatchTask(ITask task)
        {
            TaskDebug.Verbose($"TM: Dispatching task {task}");
            var workingLine = _workingLines[task.QueueTag];
            workingLine.Enqueue(task);
        }

        private void WorkingLineTaskRejectedHandler(object sender, TaskRejectedEventArgs e)
        {
            Notify(TaskRejected, e);
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskRejectedEventArgs> TaskRejected;

        private void WorkingLineTaskBeginHandler(object sender, TaskEventArgs e)
        {
            Notify(TaskBegin, e);
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskEventArgs> TaskBegin;

        private void WorkingLineTaskEndHandler(object sender, TaskEventArgs e)
        {
            Notify(TaskEnd, e);
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
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
