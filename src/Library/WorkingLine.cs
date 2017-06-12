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

        public string Tag { get; private set; }
        public IWorkerFactory WorkerFactory { get; private set; }
        public int Worker { get; private set; }
        public ThreadPriority ThreadPriority { get; private set; }

        private readonly TaskQueue _queue = new TaskQueue();
        private readonly List<WorkerThread> _threads = new List<WorkerThread>();
        private readonly Dictionary<WorkerThread, bool> _threadBusy = new Dictionary<WorkerThread, bool>();
        private readonly ManualResetEvent _busyEvent = new ManualResetEvent(true);

        public ICollection<WorkerThread> WorkerThreads => _threads.ToArray();

        public event PropertyChangedEventHandler PropertyChanged;

        public WorkingLine(string tag, IWorkerFactory factory, int worker, ThreadPriority threadPriority)
        {
            Tag = tag ?? throw new ArgumentNullException(nameof(tag));
            WorkerFactory = factory ?? throw new ArgumentNullException(nameof(factory));
            Worker = Math.Max(1, Math.Min(MAX_WORKER, worker));
            ThreadPriority = threadPriority;

            for (int i = 0; i < worker; i++)
            {
                var thread = new WorkerThread(_queue, factory.Create(), $"{tag}_{i}");
                _threadBusy[thread] = false;
                _threads.Add(thread);
                thread.BusyChanged += ThreadWorkerBusyChangedHandler;
                thread.TaskRejected += ThreadWorkerTaskRejectedHandler;
                thread.WorkerError += ThreadWorkerErrorHandler;
                thread.TaskBegin += ThreadWorkerTaskBeginHandler;
                thread.TaskEnd += ThreadWorkerTaskEndHandler;
            }
        }

        private int _busyWorkerCount;

        public int BusyWorkerCount
        {
            get => _busyWorkerCount;
            set
            {
                if (_busyWorkerCount == value) return;
                _busyWorkerCount = value;
                Busy = _busyWorkerCount > 0;
                OnBusyWorkerCountChanged();
            }
        }

        public event EventHandler BusyWorkerCountChanged;

        private void OnBusyWorkerCountChanged()
        {
            BusyWorkerCountChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BusyWorkerCount)));
        }

        private void ThreadWorkerBusyChangedHandler(object sender, EventArgs e)
        {
            var thread = (WorkerThread)sender;
            var count = 0;
            lock (_threadBusy)
            {
                _threadBusy[thread] = thread.Busy;
                foreach (var busy in _threadBusy.Values)
                {
                    if (busy) count++;
                }
            }
            BusyWorkerCount = count;
        }

        private void ThreadWorkerTaskRejectedHandler(object sender, TaskRejectedEventArgs e)
        {
            TaskRejected?.Invoke(this, e);
        }

        public event EventHandler<TaskRejectedEventArgs> TaskRejected;

        private void ThreadWorkerErrorHandler(object sender, WorkerErrorEventArgs e)
        {
            WorkerError?.Invoke(this, e);
        }

        public event EventHandler<WorkerErrorEventArgs> WorkerError;

        private void ThreadWorkerTaskBeginHandler(object sender, TaskEventArgs e)
        {
            TaskBegin?.Invoke(this, e);
        }

        public event EventHandler<TaskEventArgs> TaskBegin;

        private void ThreadWorkerTaskEndHandler(object sender, TaskEventArgs e)
        {
            TaskEnd?.Invoke(this, e);
        }

        public event EventHandler<TaskEventArgs> TaskEnd;

        private bool _busy;
        public bool Busy
        {
            get => _busy;
            set
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Busy)));
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

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(WorkingLine) + "_" + Tag);
            foreach (var thread in _threads)
            {
                thread.Start();
            }
        }

        public void Cancel()
        {
            _queue.Cancel();
            foreach (var thread in _threads)
            {
                thread.Cancel();
            }
        }

        public void WaitForEnd()
        {
            _busyEvent.WaitOne();
        }

        public void WaitForDeath()
        {
            foreach (var thread in _threads)
            {
                thread.WaitForDeath();
            }
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            _queue.Dispose();
            foreach (var thread in _threads)
            {
                thread.Dispose();
            }
            WaitForDeath();
        }
    }
}
