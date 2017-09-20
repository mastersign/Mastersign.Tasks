using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public interface IWorker
    {
        void Process(ITask task, CancelationToken cancelationToken);
    }
}
