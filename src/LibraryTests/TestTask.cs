using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    public class TestTask : TaskBase
    {
        public string Label { get; }

        public string Group { get; }

        public TestTask(string label, string queueTag = null, string group = null) 
            : base(queueTag)
        {
            Label = label;
            Group = group;
        }

        public void AddDependency(ITask task) => base.DependencyList.Add(task);

        public event EventHandler<PropertyUpdateEventArgs<TaskState>> StateChanging;

        protected override void OnStateChanged(TaskState oldValue, TaskState newValue)
        {
            Debug.WriteLine($"[{System.Threading.Thread.CurrentThread.Name}] T: Task[{Label}] State Changing: {oldValue} -> {newValue}");
            StateChanging?.Invoke(this, new PropertyUpdateEventArgs<TaskState>(nameof(ITask.State), oldValue, newValue));
            Debug.WriteLine($"[{System.Threading.Thread.CurrentThread.Name}] T: Task[{Label}] State Changed: {oldValue} -> {newValue}");
            base.OnStateChanged(oldValue, newValue);
        }

        public override string ToString() => $"Task[{Label}]({State})";
    }
}
