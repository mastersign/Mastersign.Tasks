using System;
using System.Collections.Generic;
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

        public event EventHandler<TaskEventArgs> StateChanging;

        protected override void OnStateChanged()
        {
            StateChanging?.Invoke(this, new TaskEventArgs(this));
            base.OnStateChanged();
        }
    }
}
