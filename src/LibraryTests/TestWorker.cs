using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    class TestWorker : IWorker
    {
        public string QueueTag { get; private set; }

        public TestWorker(string tag)
        {
            QueueTag = tag;
        }

        public void Process(ITask task, CancelationToken cancelationToken)
        {
            var t = (TaskBase)task;
            for (int i = 0; i < 1000; i++)
            {
                if (cancelationToken.IsCanceled) break;
                t.UpdateProgress(i.ToString(), i / 1000f);
                HardWork.DoRandomAmount();
            }
            t.UpdateProgress("finished", 1f);
            t.UpdateState(TaskState.CleaningUp);
            HardWork.DoRandomAmount();
        }
    }
}
