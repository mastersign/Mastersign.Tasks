using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mastersign.Tasks.Test.Monitors;
using static Mastersign.Tasks.Test.Monitors.EventRecordPredicates;
using static Mastersign.Tasks.Test.StateAssertions;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class WorkerThreadTest
    {
        private void AssertTaskProgressRange(EventMonitor<TestTask> taskMonitor, float first = 0f, float last = 1f)
        {
            var progressHistory = taskMonitor.FilterHistory(ByPropertyChanges<float>(nameof(TestTask.Progress)));
            Assert.IsTrue(progressHistory.Count > 0, "No progress events fired.");
            Assert.AreEqual(first, progressHistory.First.GetNewValue<float>(), $"The first progress event did not signal {first}.");
            Assert.AreEqual(last, progressHistory.Last.GetNewValue<float>(), $"The last progress event did not signal {last}.");
        }

        private WorkerThread CreateWorkerThread()
        {
            const string NAME = "TestABC123";

            var q = new TaskQueue();
            Assert.IsTrue(q.IsEmpty);

            var w = new TestWorker();
            var wt = new WorkerThread(q, w, NAME);

            Assert.AreEqual(q, wt.Queue);
            Assert.IsTrue(wt.Name.EndsWith(NAME));

            AssertState(wt, isDisposed: false, isAlive: false, busy: false);
            return wt;
        }

        private void WithWorkerThread(Action<WorkerThread, EventMonitor<WorkerThread>> testCase, bool started = true)
        {
            var wt = CreateWorkerThread();
            var wtMon = new EventMonitor<WorkerThread>(wt);

            if (started)
            {
                wt.Start();
                AssertState(wt, isDisposed: false, isAlive: true, busy: false);
                wtMon.History.AssertSender(wt);
                wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                    .AssertPropertyValues(true);
            }
            wtMon.ClearHistory();

            testCase(wt, wtMon);

            wtMon.ClearHistory();

            wt.Queue.Dispose();
            wt.Dispose();
            AssertState(wt, isDisposed: true, isAlive: false, busy: false);
            wtMon.History.AssertSender(wt);
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                .AssertPropertyValues(false);
        }

        [TestMethod]
        [TestCategory("LifeCycle")]
        public void LifeCycleTest()
            => WithWorkerThread(LifeCycleTestCase);

        private void LifeCycleTestCase(WorkerThread wt, EventMonitor<WorkerThread> wtMon)
        {
            // nothing
        }

        [TestMethod]
        public void SingleTaskTest()
            => WithWorkerThread(SingleTaskTestCase, started: false);

        private void SingleTaskTestCase(WorkerThread wt, EventMonitor<WorkerThread> wtMon)
        {
            var q = wt.Queue;
            var task = new TestTask("single");
            var taskMon = new EventMonitor<TestTask>(task);

            q.Enqueue(task);

            Assert.IsFalse(q.IsEmpty);
            AssertState(wt, isDisposed: false, isAlive: false, busy: false);

            wt.Start();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                .AssertPropertyValues(true);

            //WaitFor(() => task.State == TaskState.Succeeded, 1000);
            wt.WaitForEnd(timeout: 10000);
            
            Assert.IsTrue(q.IsEmpty);
            AssertState(wt, isDisposed: false, isAlive: true, busy: false);

            var wtHist = wtMon.History;
            wtHist.AssertSender(wt);
            wtHist.AssertEventNames(
                nameof(wt.IsAliveChanged),
                nameof(wt.BusyChanged),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.TaskBegin),
                nameof(wt.TaskEnd),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.BusyChanged));
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                .AssertPropertyValues(true);
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.Busy)))
                .AssertPropertyValues(true, false);
            wtMon.FilterHistory(ByPropertyChanges<ITask>(nameof(WorkerThread.CurrentTask)))
                .AssertPropertyValues(task, null);

            taskMon.History.AssertSender(task);

            taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(TestTask.State)))
                .AssertPropertyValues(
                    TaskState.InProgress,
                    TaskState.CleaningUp,
                    TaskState.Succeeded
                );
            AssertTaskProgressRange(taskMon);
        }

        [TestMethod]
        public void MultiTaskTest()
            => WithWorkerThread(MultiTaskTestCase);

        private void MultiTaskTestCase(WorkerThread wt, EventMonitor<WorkerThread> wtMon)
        {
            var q = wt.Queue;
            var tasks = new[] {
                new TestTask("multi 1"),
                new TestTask("multi 2"),
                new TestTask("multi 3")
            };
            var taskMons = tasks.Select(t => new EventMonitor<TestTask>(t)).ToArray();

            foreach (var task in tasks)
            {
                q.Enqueue(task);
            }

            //WaitFor(() => tasks.All(t => t.State == TaskState.Succeeded), 10000);
            Assert.IsTrue(wt.WaitForEnd(timeout: 10000));

            Assert.IsTrue(q.IsEmpty);
            AssertState(wt, isDisposed: false, isAlive: true, busy: false);

            var wtHist = wtMon.History;
            wtHist.AssertSender(wt);
            wtHist.AssertEventNames(
                nameof(WorkerThread.BusyChanged),
                nameof(WorkerThread.CurrentTaskChanged),
                nameof(WorkerThread.TaskBegin),
                nameof(WorkerThread.TaskEnd),
                nameof(WorkerThread.CurrentTaskChanged),
                nameof(WorkerThread.TaskBegin),
                nameof(WorkerThread.TaskEnd),
                nameof(WorkerThread.CurrentTaskChanged),
                nameof(WorkerThread.TaskBegin),
                nameof(WorkerThread.TaskEnd),
                nameof(WorkerThread.CurrentTaskChanged),
                nameof(WorkerThread.BusyChanged));
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.Busy)))
                .AssertPropertyValues(true, false);
            wtMon.FilterHistory(ByPropertyChanges<ITask>(nameof(WorkerThread.CurrentTask)))
                .AssertPropertyValues(tasks.Concat(new TestTask[] { null }).ToArray());

            for (int i = 0; i < tasks.Length; i++)
            {
                var taskMon = taskMons[i];
                var task = tasks[i];
                var taskHist = taskMon.History;
                taskHist.AssertSender(task);
                var taskStateHist = taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)));
                taskStateHist.AssertPropertyValues(
                    TaskState.InProgress,
                    TaskState.CleaningUp,
                    TaskState.Succeeded);
                var taskProgressHist = taskMon.FilterHistory(ByPropertyChanges<float>(nameof(ITask.Progress)));
                Assert.IsTrue(taskProgressHist.Count > 0);
                Assert.AreEqual(0f, taskProgressHist.First.GetNewValue<float>());
                Assert.AreEqual(1f, taskProgressHist.Last.GetNewValue<float>());
            }
        }

        // TODO test worker error handling

        // TODO test cancellation
    }
}
