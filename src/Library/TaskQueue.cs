using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    internal class TaskQueue
    {
        private readonly Queue<ITask> _tasks = new Queue<ITask>();

        public string Tag { get; set; }

        public event EventHandler NewTask;

        public bool IsEmpty
        {
            get
            {
                lock(_tasks)
                {
                    return _tasks.Count == 0;
                }
            }
        }

        public void Enqueue(ITask task)
        {
            var notify = false;
            lock (_tasks)
            {
                if (_tasks.Count == 0) notify = true;
                _tasks.Enqueue(task);
            }
            if (notify)
            {
                NewTask?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TryDequeue(ref ITask task)
        {
            lock (_tasks)
            {
                if (_tasks.Count > 0)
                {
                    task = _tasks.Dequeue();
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}