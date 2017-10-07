using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class TaskQueue : ConcurrentDispatcher<ITask>
    {
        public void Cancel()
        {
            var tasks = DequeueAll();
            foreach (var t in tasks)
            {
                t.UpdateState(TaskState.Obsolete);
            }
        }
    }
}