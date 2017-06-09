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
        private readonly ManualResetEvent _busyEvent = new ManualResetEvent(true);

        public WorkerThread[] WorkerThreads => _threads.ToArray();

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
                _threads.Add(thread);
                thread.BusyChanged += ThreadWorkerBusyChangedHandler;
            }
        }

        private void ThreadWorkerBusyChangedHandler(object sender, EventArgs e)
        {
            var count = 0;
            foreach (var thread in _threads)
            {
                if (thread.Busy) count++;
            }
            BusyWorkerCount = count;
        }

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

        public void WaitForFinish()
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

            foreach (var thread in _threads)
            {
                thread.Dispose();
            }
            WaitForDeath();
        }
    }
}
