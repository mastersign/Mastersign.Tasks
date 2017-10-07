using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public static class TaskHelper
    {
        public static IEnumerable<ITask> Responsibilities(ITask task, IEnumerable<ITask> allTasks)
        {
            foreach (var t in allTasks)
            {
                if (t == task) continue;
                foreach (var d in t.Dependencies)
                {
                    if (d == task)
                    {
                        yield return t;
                    }
                }
            }
        }
    }
}
