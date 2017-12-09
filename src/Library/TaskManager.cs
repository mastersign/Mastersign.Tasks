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

            CheckStartPreconditions();

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

                InitializeRunningState();
                InitializeTaskWatchers();

                NotifyInitiallyReadyTasks();
            });
        }

        private void CheckStartPreconditions()
        {
            foreach (var t in _tasks)
            {
                if (t.State != TaskState.Waiting)
                {
                    throw new InvalidOperationException(
                        $"The task '{t}' is not in waiting state.");
                }
                if (!_workingLines.ContainsKey(t.QueueTag))
                {
                    throw new InvalidOperationException(
                        $"The task '{t}' has the queue tag '{t.QueueTag}', but for this tag exists no working line.");
                }
            }
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler Started;

        public void Cancel() => _innerEventLoop.RunActionAsync(ProcessCancel);

        /// <remarks>Runs on the inner event loop.</remarks>
        private void ProcessCancel()
        {
            _innerEventLoop.AssertThread();

            _isCancelled = true;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Cancel();
            }
            _outerEventLoop.FireEvent(this, Canceled);

            foreach (var taskWatcher in _taskWatchers)
            {
                if (!taskWatcher.IsReady)
                {
                    taskWatcher.Task.UpdateState(TaskState.Obsolete);
                }
            }

            CheckForEnd();
        }

        public event EventHandler Canceled;

        /// <remarks>
        /// Waiting for the end of the task manager activity does NOT mean, all queued events are fired.
        /// </remarks>
        public bool WaitForEnd(int timeout = Timeout.Infinite)
        {
            try
            {
                return _isRunningEvent.WaitOne(timeout) && _innerEventLoop.WaitForEmpty(timeout);
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

        private int taskCount;
        private int waitingTaskCount;
        private int runningTaskCount;
        private int endedTaskCount;

        /// <remarks>Runs on the inner event loop.</remarks>
        private void InitializeRunningState()
        {
            _innerEventLoop.AssertThread();

            taskCount = _tasks.Count;
            waitingTaskCount = taskCount;
            runningTaskCount = 0;
            endedTaskCount = 0;
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void InitializeTaskWatchers()
        {
            _innerEventLoop.AssertThread();

            _taskWatchers.Clear();
            foreach (var t in _tasks)
            {
                var taskWatcher = new TaskWatcher(t, _innerEventLoop);
                taskWatcher.GotReady += TaskGotReadyHandler;
                taskWatcher.GotObsolete += TaskGotObsoleteHandler;
                taskWatcher.Begin += TaskStartedHandler;
                taskWatcher.End += TaskFinishedHandler;
                taskWatcher.ProgressChanged += TaskProgressChangedHandler;
                taskWatcher.ProgressMessageChanged += TaskProgressMessageChangedHandler;
                _taskWatchers.Add(taskWatcher);
                TaskDebug.Verbose($"Watching task {t}");
            }
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void NotifyInitiallyReadyTasks()
        {
            _innerEventLoop.AssertThread();

            foreach (var taskWatcher in _taskWatchers.ToArray())
            {
                if (taskWatcher.IsReady)
                {
                    TaskGotReadyHandler(taskWatcher, new TaskEventArgs(taskWatcher.Task));
                }
            }
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void RemoveTaskWatcher(TaskWatcher taskWatcher)
        {
            _innerEventLoop.AssertThread();

            taskWatcher.GotReady -= TaskGotReadyHandler;
            taskWatcher.GotObsolete -= TaskGotObsoleteHandler;
            taskWatcher.End -= TaskFinishedHandler;
            taskWatcher.ProgressChanged -= TaskProgressChangedHandler;
            taskWatcher.ProgressMessageChanged -= TaskProgressMessageChangedHandler;
            _taskWatchers.Remove(taskWatcher);
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void TaskGotReadyHandler(object sender, TaskEventArgs e)
        {
            _innerEventLoop.AssertThread();

            var taskWatcher = (TaskWatcher)sender;

            Debug.Assert(taskWatcher.IsReady,
                "A task watcher was reported as ready, but it is not.");

            TaskDebug.Verbose($"TM: {e.Task} got ready");

            if (!_isCancelled)
            {
                DispatchTask(e.Task);
            }
            else
            {
                e.Task.UpdateState(TaskState.Obsolete);
            }
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void TaskGotObsoleteHandler(object sender, TaskEventArgs e)
        {
            _innerEventLoop.AssertThread();

            var taskWatcher = (TaskWatcher)sender;

            Debug.Assert(taskWatcher.IsObsolete,
                "A task watcher was reported as obsolete, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} got obsolete");
            _outerEventLoop.FireEvent(this, TaskEnd, e);

            waitingTaskCount--;
            Debug.Assert(waitingTaskCount >= 0, "Error during task count. Waiting task count is negative.");
            endedTaskCount++;
            Debug.Assert(endedTaskCount <= taskCount, "Error during task count. Ended task count is greater than total task count.");
            Debug.Assert(waitingTaskCount + runningTaskCount + endedTaskCount == taskCount,
                "Error during task count. The numbers do not add up.");

            ProcessEndedTask(taskWatcher);

            CheckForEnd();
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void TaskStartedHandler(object sender, TaskEventArgs e)
        {
            _innerEventLoop.AssertThread();

            var taskWatcher = (TaskWatcher)sender;

            Debug.Assert(taskWatcher.HasBegun,
                "A task watcher was reported as started, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} started");
            _outerEventLoop.FireEvent(this, TaskBegin, e);

            waitingTaskCount--;
            Debug.Assert(waitingTaskCount >= 0, "Error during task count. Waiting task count is negative.");
            runningTaskCount++;
            Debug.Assert(runningTaskCount <= taskCount, "Error during task count. Running task count is greater than total task count.");
            Debug.Assert(waitingTaskCount + runningTaskCount + endedTaskCount == taskCount,
                "Error during task count. The numbers do not add up.");
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void TaskFinishedHandler(object sender, TaskEventArgs e)
        {
            _innerEventLoop.AssertThread();

            var taskWatcher = (TaskWatcher)sender;

            Debug.Assert(taskWatcher.HasEnded,
                "A task watcher was reported as finished, but it is not.");

            TaskDebug.Verbose($"TM: {taskWatcher.Task} finished");
            _outerEventLoop.FireEvent(this, TaskEnd, e);

            runningTaskCount--;
            Debug.Assert(runningTaskCount >= 0, "Error during task count. Running task count is negative.");
            endedTaskCount++;
            Debug.Assert(endedTaskCount <= taskCount, "Error during task count. Ended task count is greater than total task count.");
            Debug.Assert(waitingTaskCount + runningTaskCount + endedTaskCount == taskCount,
                "Error during task count. The numbers do not add up.");

            ProcessEndedTask(taskWatcher);

            CheckForEnd();
        }

        private void TaskProgressChangedHandler(object sender, TaskPropertyUpdateEventArgs<float> e)
            => _outerEventLoop.FireEvent(this, TaskProgressChanged, e);

        private void TaskProgressMessageChangedHandler(object sender, TaskPropertyUpdateEventArgs<string> e)
            => _outerEventLoop.FireEvent(this, TaskProgressMessageChanged, e);

        /// <remarks>Runs on the inner event loop.</remarks>
        private void DispatchTask(ITask task)
        {
            _innerEventLoop.AssertThread();

            TaskDebug.Verbose($"TM: Dispatching task {task}");
            var workingLine = _workingLines[task.QueueTag];
            workingLine.Enqueue(task);
        }

        private void ProcessEndedTask(TaskWatcher taskWatcher)
            => ProcessEndedTasks(new[] { taskWatcher });

        /// <remarks>Runs on the inner event loop.</remarks>
        private void ProcessEndedTasks(IEnumerable<TaskWatcher> taskWatchers)
        {
            _innerEventLoop.AssertThread();

            foreach (var taskWatcher in taskWatchers)
            {
                RemoveTaskWatcher(taskWatcher);
                TaskDebug.Verbose($"TM: State after task end: waiting={waitingTaskCount}, running={runningTaskCount}, ended={endedTaskCount}");
            }
        }

        /// <remarks>Runs on the inner event loop.</remarks>
        private void CheckForEnd()
        {
            _innerEventLoop.AssertThread();
            if (waitingTaskCount == 0 &&
                runningTaskCount == 0 &&
                endedTaskCount == taskCount)
            {
                IsRunning = false;
            }
        }

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskEventArgs> TaskBegin;

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskEventArgs> TaskEnd;

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskPropertyUpdateEventArgs<float>> TaskProgressChanged;

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<TaskPropertyUpdateEventArgs<string>> TaskProgressMessageChanged;

        private void WorkingLineWorkerErrorHandler(object sender, WorkerErrorEventArgs e)
            => _outerEventLoop.FireEvent(this, WorkerError, e);

        /// <remarks>
        /// This event is fired from e decoupled thread.
        /// Which means, that the state of the <see cref="TaskManager"/> may have changed again,
        /// in the time when the event handler is executed.
        /// </remarks>
        public event EventHandler<WorkerErrorEventArgs> WorkerError;

        #endregion
    }
}
