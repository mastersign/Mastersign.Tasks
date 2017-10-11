using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class WorkerThread : IDisposable
    {
        private TaskQueue _queue;
        private IWorker _worker;
        private string _label;
        private Thread _thread;
        private ManualResetEvent _aliveEvent = new ManualResetEvent(true);
        private ManualResetEvent _workedEvent = new ManualResetEvent(false);
        private ManualResetEvent _busyEvent = new ManualResetEvent(true);
        private CancelationToken _cancelationToken;

        private ThreadPriority ThreadPriority { get; set; }

        public WorkerThread(TaskQueue queue, IWorker worker, string label)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
            _label = label;
            ThreadPriority = ThreadPriority.Normal;
        }

        public TaskQueue Queue => _queue;

        public bool IsAlive => _thread != null;

        public event EventHandler<PropertyUpdateEventArgs<bool>> IsAliveChanged;

        private void OnIsAliveChanged(bool oldValue, bool newValue)
        {
            IsAliveChanged?.Invoke(this, new PropertyUpdateEventArgs<bool>(nameof(IsAlive), oldValue, newValue));
        }

        public event EventHandler<TaskEventArgs> TaskBegin;

        private void OnTaskBegin(ITask task)
        {
            TaskBegin?.Invoke(this, new TaskEventArgs(task, TaskState.InProgress));
            task.UpdateState(TaskState.InProgress);
        }

        public event EventHandler<TaskEventArgs> TaskEnd;

        private void OnTaskEnd(ITask task, TaskState endState, Exception workerError)
        {
            try
            {
                OnWorkerError(workerError);
            }
            catch (Exception)
            {
                // ignore exceptions during event handling
            }
            task.UpdateState(endState);
            TaskEnd?.Invoke(this, new TaskEventArgs(task, endState));
        }

        private bool _busy;
        public bool Busy
        {
            get => _busy;
            private set
            {
                if (_busy == value) return;
                var oldValue = _busy;
                _busy = value;
                OnBusyChanged(oldValue, value);
                if (_busy)
                    _busyEvent.Reset();
                else
                    _busyEvent.Set();
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<bool>> BusyChanged;

        private void OnBusyChanged(bool oldValue, bool newValue)
        {
            BusyChanged?.Invoke(this, new PropertyUpdateEventArgs<bool>(nameof(Busy), oldValue, newValue));
        }

        private ITask _currentTask;
        public ITask CurrentTask
        {
            get => _currentTask;
            set
            {
                if (_currentTask == value) return;
                var oldValue = _currentTask;
                _currentTask = value;
                OnCurrentTaskChanged(oldValue, value);
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<ITask>> CurrentTaskChanged;

        private void OnCurrentTaskChanged(ITask oldValue, ITask newValue)
        {
            CurrentTaskChanged?.Invoke(this, new PropertyUpdateEventArgs<ITask>(nameof(CurrentTask), oldValue, newValue));
        }

        public string Name => nameof(WorkerThread) + (string.IsNullOrEmpty(_label) ? string.Empty : "_" + _label);

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(Name);
            if (IsAlive) throw new InvalidOperationException("The thread is already started.");
            _cancelationToken = new CancelationToken();
            _thread = new Thread(ThreadLoop);
            _thread.Name = Name;
            _thread.Priority = ThreadPriority;
            _aliveEvent.Reset();
            OnIsAliveChanged(false, true);
            _thread.Start();
        }

        private void ThreadLoop()
        {
            ITask task = null;
            while (!_cancelationToken.IsCanceled && !_queue.IsDisposed && !IsDisposed)
            {
                while (!_cancelationToken.IsCanceled && _queue.TryDequeue(ref task))
                {
                    var taskState = task.State;
                    if (taskState != TaskState.Waiting)
                    {
                        OnTaskRejected(task, taskState);
                        continue;
                    }
                    Busy = true;
                    _workedEvent.Set();
                    CurrentTask = task;
                    OnTaskBegin(task);
                    Exception workerError = null;
                    try
                    {
                        _worker.Process(task, _cancelationToken);
                    }
                    catch (Exception e)
                    {
                        workerError = e;
                    }
                    var endState = task.State;
                    if (endState == TaskState.InProgress || endState == TaskState.CleaningUp)
                    {
                        if (workerError != null)
                            endState = TaskState.Failed;
                        else if (_cancelationToken.IsCanceled)
                            endState = TaskState.Canceled;
                        else
                            endState = TaskState.Succeeded;
                    }
                    OnTaskEnd(task, endState, workerError);
                }
                CurrentTask = null;
                Busy = false;
                if (_cancelationToken.IsCanceled || IsDisposed || _queue.IsDisposed) break;
                _queue.WaitForNewItem();
            }
            CurrentTask = null;
            Busy = false;

            _thread = null;
            OnIsAliveChanged(true, false);
            _aliveEvent.Set();
        }

        public event EventHandler<TaskRejectedEventArgs> TaskRejected;

        private void OnTaskRejected(ITask task, TaskState rejectedState)
        {
            TaskRejected?.Invoke(this, new TaskRejectedEventArgs(task, rejectedState));
        }

        public event EventHandler<WorkerErrorEventArgs> WorkerError;

        private void OnWorkerError(Exception e)
        {
            WorkerError?.Invoke(this, new WorkerErrorEventArgs(e));
        }

        public event EventHandler Cancelled;

        private void OnCancelled()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        public void Cancel()
        {
            _cancelationToken?.Cancel();
            OnCancelled();
        }

        public bool WaitForEnd(bool mustHaveWorked = true, int timeout = Timeout.Infinite)
        {
            try
            {
                if (mustHaveWorked)
                {
                    _workedEvent.WaitOne(timeout);
                }
                return _busyEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        public bool WaitForDeath(bool mustHaveWorked = false, int timeout = Timeout.Infinite)
        {
            try
            {
                if (mustHaveWorked)
                {
                    _workedEvent.WaitOne(timeout);
                }
                return _aliveEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        public bool IsDisposed { get; private set; }
        private readonly object _disposeLock = new object();

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (IsDisposed) return;
                IsDisposed = true;
            }
            WaitForDeath();
            _cancelationToken = null;
            _aliveEvent.Close();
            _workedEvent.Close();
            _busyEvent.Close();
        }
    }
}
