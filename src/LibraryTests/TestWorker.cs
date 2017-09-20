using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    class TestWorker : IWorker
    {
        public void Process(ITask task, CancelationToken cancelationToken)
        {
            var t = (TaskBase)task;
            t.UpdateProgress("starting", 0f);
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
