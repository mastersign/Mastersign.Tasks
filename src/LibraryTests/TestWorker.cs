using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    class TestWorker : IWorker
    {
        private void Log(TestTask t, string message) => TaskDebug.Verbose($"TA: Task[{t.Label}] {message}");

        public void Process(ITask task, CancelationToken cancelationToken)
        {
            var t = (TestTask)task;
            t.UpdateProgress("starting", 0f);
            for (int i = 0; i < 1000; i++)
            {
                if (cancelationToken.IsCanceled)
                {
                    Log(t, "CANCELLED");
                    break;
                }
                t.UpdateProgress(i.ToString(), i / 1000f);
                HardWork.DoRandomAmount();
            }
            t.UpdateProgress("finished", 1f);
            t.UpdateState(TaskState.CleaningUp);
            HardWork.DoRandomAmount();
            Log(t, "FINISHED");
        }
    }
}
