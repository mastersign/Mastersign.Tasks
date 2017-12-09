using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class WorkingLine : IDisposable, INotifyPropertyChanged
    {
        public const int MAX_WORKER = 1024;

        public string QueueTag { get; private set; }
        public IWorkerFactory WorkerFactory { get; private set; }
        public int Worker { get; private set; }
        public ThreadPriority ThreadPriority { get; private set; }

        private readonly EventLoop _eventLoop;
        private readonly TaskQueue _queue = new TaskQueue();
        private readonly List<WorkerThread> _threads = new List<WorkerThread>();
        private readonly Dictionary<WorkerThread, bool> _threadBusy = new Dictionary<WorkerThread, bool>();
        private readonly ManualResetEvent _workedEvent = new ManualResetEvent(false);
        private readonly ManualResetEvent _busyEvent = new ManualResetEvent(true);

        public TaskQueue Queue => _queue;

        public ICollection<WorkerThread> WorkerThreads => _threads.ToArray();

        public event PropertyChangedEventHandler PropertyChanged;

        public WorkingLine(EventLoop eventLoop, string queueTag, IWorkerFactory factory, int worker = 1, ThreadPriority threadPriority = ThreadPriority.Normal)
        {
            QueueTag = queueTag ?? throw new ArgumentNullException(nameof(queueTag));
            WorkerFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            Worker = Math.Max(1, Math.Min(MAX_WORKER, worker));
            ThreadPriority = threadPriority;
            _eventLoop = eventLoop;

            for (int i = 0; i < worker; i++)
            {
                var thread = new WorkerThread(_queue, factory.Create(), $"{queueTag}_{i:0000}");
                _threadBusy[thread] = false;
                _threads.Add(thread);
                thread.BusyChanged += WorkerThreadBusyChangedHandler;
                thread.TaskRejected += WorkerThreadTaskRejectedHandler;
                thread.WorkerError += WorkerThreadErrorHandler;
                thread.TaskBegin += WorkerThreadTaskBeginHandler;
                thread.TaskEnd += WorkerThreadTaskEndHandler;
            }
        }

        private int _busyWorkerCount;

        public int BusyWorkerCount
        {
            get => _busyWorkerCount;
            private set
            {
                if (_busyWorkerCount == value) return;
                var oldValue = _busyWorkerCount;
                _busyWorkerCount = value;
                if (_busyWorkerCount > 0)
                {
                    Busy = true;
                    OnBusyWorkerCountChanged(oldValue, value);
                }
                else
                {
                    OnBusyWorkerCountChanged(oldValue, value);
                    Busy = false;
                }
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<int>> BusyWorkerCountChanged;

        private void OnBusyWorkerCountChanged(int oldValue, int newValue)
        {
            _eventLoop.Push(BusyWorkerCountChanged, this, new PropertyUpdateEventArgs<int>(nameof(BusyWorkerCount), oldValue, newValue));
            _eventLoop.Push(PropertyChanged, this, new PropertyChangedEventArgs(nameof(BusyWorkerCount)));
        }

        private void WorkerThreadBusyChangedHandler(object sender, PropertyUpdateEventArgs<bool> e)
            => _eventLoop.RunActionAsync(() => ProcessWorkerThreadBusyChanged((WorkerThread)sender, e.OldValue, e.NewValue));

        private void ProcessWorkerThreadBusyChanged(WorkerThread thread, bool oldValue, bool newValue)
        {
            var threadIsBusy = newValue;
            var count = 0;
            _threadBusy[thread] = threadIsBusy;
            foreach (var busy in _threadBusy.Values)
            {
                if (busy) count++;
            }
            BusyWorkerCount = count;
            if (threadIsBusy)
            {
                _workedEvent.Set();
            }
        }

        private void WorkerThreadTaskRejectedHandler(object sender, TaskRejectedEventArgs e)
            => _eventLoop.Push(TaskRejected, this, e);

        public event EventHandler<TaskRejectedEventArgs> TaskRejected;

        private void WorkerThreadErrorHandler(object sender, WorkerErrorEventArgs e)
            => _eventLoop.Push(WorkerError, this, e);

        public event EventHandler<WorkerErrorEventArgs> WorkerError;

        private void WorkerThreadTaskBeginHandler(object sender, TaskEventArgs e)
            => _eventLoop.Push(TaskBegin, this, e);

        public event EventHandler<TaskEventArgs> TaskBegin;

        private void WorkerThreadTaskEndHandler(object sender, TaskEventArgs e)
            => _eventLoop.Push(TaskEnd, this, e);

        public event EventHandler<TaskEventArgs> TaskEnd;

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
            _eventLoop.Push(BusyChanged, this, new PropertyUpdateEventArgs<bool>(nameof(Busy), oldValue, newValue));
            _eventLoop.Push(PropertyChanged, this, new PropertyChangedEventArgs(nameof(Busy)));
        }

        public ITask[] CurrentTasks
        {
            get
            {
                var result = new List<ITask>();
                foreach (var thread in _threads)
                {
                    var task = thread.CurrentTask;
                    if (task != null) result.Add(task);
                }
                return result.ToArray();
            }
        }

        public void Enqueue(ITask task)
        {
            _queue.Enqueue(task);
        }

        public void Enqueue(IEnumerable<ITask> tasks)
        {
            foreach (var task in tasks)
            {
                Enqueue(task);
            }
        }

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(WorkingLine) + "_" + QueueTag);
            foreach (var thread in _threads)
            {
                thread.Start();
            }
        }

        public event EventHandler Cancelled;

        private void OnCancelled()
            => _eventLoop.Push(Cancelled, this, EventArgs.Empty);

        public void Cancel()
        {
            _queue.Cancel();
            foreach (var thread in _threads)
            {
                thread.Cancel();
            }
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
                return _busyEvent.WaitOne(timeout) && _eventLoop.WaitForEmpty(timeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            };
        }

        public bool WaitForDeath(int timeout = Timeout.Infinite)
        {
            var result = true;
            foreach (var thread in _threads)
            {
                result &= thread.WaitForDeath(mustHaveWorked: false, timeout: timeout);
            }
            return result;
        }

        private int _disposed = 0;
        public bool IsDisposed => _disposed != 0;

        public void Dispose()
        {
            var disposed = Interlocked.Exchange(ref _disposed, 1);
            if (disposed != 0) return;
            _queue.Dispose();
            foreach (var thread in _threads)
            {
                thread.Dispose();
            }
            WaitForDeath();
            _workedEvent.Close();
            _busyEvent.Close();
        }
    }
}
