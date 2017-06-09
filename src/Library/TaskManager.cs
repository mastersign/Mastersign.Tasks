using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class TaskManager : IDisposable
    {
        private readonly List<ITask> _tasks = new List<ITask>();
        private readonly Dictionary<string, WorkingLine> _workingLines = new Dictionary<string, WorkingLine>();

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                OnIsRunningChanged();
            }
        }

        public event EventHandler IsRunningChanged;

        private void OnIsRunningChanged()
        {
            IsRunningChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddTask(ITask task)
        {
            if (_isRunning) throw new InvalidOperationException("Tasks can not be added after the manager was started.");
            _tasks.Add(task);
        }

        public void AddTasks(IEnumerable<ITask> tasks)
        {
            foreach (var task in tasks) AddTask(task);
        }

        public void AddWorkingLine(string tag, IWorkerFactory factory, int worker, ThreadPriority threadPriority)
        {
            if (_isRunning) throw new InvalidOperationException("Working lines can not be added after the manager was started.");
            if (_workingLines.ContainsKey(tag)) throw new ArgumentException("The given tag is already in use for another working line.", nameof(tag));
            var workingLine = new WorkingLine(tag, factory, worker, threadPriority);
            _workingLines[tag] = workingLine;
        }

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(TaskManager));
            if (_isRunning) throw new InvalidOperationException("The manager was already started.");
            _isRunning = true;
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Start();
            }
        }

        public void Cancel()
        {
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.Cancel();
            }
        }

        public void WaitForFinish()
        {
            foreach (var workingLine in _workingLines.Values)
            {
                workingLine.WaitForFinish();
            }
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
