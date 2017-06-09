using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public interface IWorker
    {
        string QueueTag { get; }

        void Process(ITask task, CancelationToken cancelationToken);
    }
}
