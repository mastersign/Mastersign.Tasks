using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class DependencyFailedException : Exception
    {
        public ITask FailedTask { get; private set; }

        public DependencyFailedException(ITask failedTask)
            : base("A dependency of the task failed.", failedTask.Error)
        {
            FailedTask = failedTask ?? throw new ArgumentNullException();
        }
    }
}
