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
        private ManualResetEvent _startedEvent = new ManualResetEvent(false);
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

        public bool IsAlive => _thread != null;

        public event EventHandler IsAliveChanged;

        private void OnIsAliveChanged()
        {
            IsAliveChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler<TaskEventArgs> TaskBegin;

        private void OnTaskBegin(ITask task)
        {
            TaskBegin?.Invoke(this, new TaskEventArgs(task));
        }

        public event EventHandler<TaskEventArgs> TaskEnd;

        private void OnTaskEnd(ITask task)
        {
            TaskEnd?.Invoke(this, new TaskEventArgs(task));
        }

        private bool _busy;
        public bool Busy
        {
            get => _busy;
            private set
            {
                if (_busy == value) return;
                _busy = value;
                OnBusyChanged();
                if (_busy)
                    _busyEvent.Reset();
                else
                    _busyEvent.Set();
            }
        }

        public event EventHandler BusyChanged;

        private void OnBusyChanged()
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        private ITask _currentTask;
        public ITask CurrentTask
        {
            get => _currentTask;
            set
            {
                if (_currentTask == value) return;
                _currentTask = value;
                OnCurrentTaskChanged();
            }
        }

        public event EventHandler CurrentTaskChanged;

        private void OnCurrentTaskChanged()
        {
            CurrentTaskChanged?.Invoke(this, EventArgs.Empty);
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
            OnIsAliveChanged();
            _thread.Start();
        }

        private void ThreadLoop()
        {
            ITask task = null;
            while (!_cancelationToken.IsCanceled && !_queue.IsDisposed && !IsDisposed)
            {
                while (_queue.TryDequeue(ref task))
                {
                    var taskState = task.State;
                    if (taskState != TaskState.Waiting)
                    {
                        OnTaskRejected(task, taskState);
                        continue;
                    }
                    Busy = true;
                    _startedEvent.Set();
                    CurrentTask = task;
                    task.UpdateState(TaskState.InProgress);
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
                    if (workerError != null)
                    {
                        if (task.State == TaskState.InProgress || task.State == TaskState.CleaningUp)
                        {
                            task.UpdateState(TaskState.Failed, workerError);
                        }
                        try
                        {
                            OnWorkerError(workerError);
                        }
                        catch (Exception)
                        {
                            // ignore exceptions during event handling
                        }
                    }
                    else if (task.State == TaskState.InProgress || task.State == TaskState.CleaningUp)
                    {
                        task.UpdateState(_cancelationToken.IsCanceled
                            ? TaskState.Canceled
                            : TaskState.Succeeded);
                    }
                    OnTaskEnd(task);
                }
                CurrentTask = null;
                Busy = false;
                if (_cancelationToken.IsCanceled || IsDisposed || _queue.IsDisposed) break;
                _queue.WaitForNewItem();
            }
            CurrentTask = null;
            Busy = false;

            _thread = null;
            OnIsAliveChanged();
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

        public void Cancel()
        {
            _cancelationToken?.Cancel();
        }

        public void WaitForEnd(bool mustHaveWorked = true, int timeout = Timeout.Infinite)
        {
            try
            {
                if (mustHaveWorked)
                {
                    _startedEvent.WaitOne(timeout);
                }
                _busyEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException) { }
        }

        public void WaitForDeath(bool mustHaveWorked = false, int timeout = Timeout.Infinite)
        {
            try
            {
                if (mustHaveWorked)
                {
                    _startedEvent.WaitOne(timeout);
                }
                _aliveEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException) { }
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            WaitForDeath();
            _cancelationToken = null;
            _aliveEvent.Close();
            _startedEvent.Close();
            _busyEvent.Close();
        }
    }
}
