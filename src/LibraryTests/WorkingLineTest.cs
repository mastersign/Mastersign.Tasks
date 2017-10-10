using Mastersign.Tasks.Test.Monitors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Mastersign.Tasks.Test.StateAssertions;
using static Mastersign.Tasks.Test.Monitors.EventRecordPredicates;
using System.Threading;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class WorkingLineTest
    {
        private static WorkingLine CreateWorkingLine(int worker)
        {
            const string TAG = "Test";
            var wf = new TestWorkerFactory();
            var wl = new WorkingLine(TAG, wf, worker: worker);
            Assert.AreEqual(wf, wl.WorkerFactory);
            Assert.AreEqual(worker, wl.Worker);
            Assert.AreEqual(wl.Worker, wl.WorkerThreads.Count);
            return wl;
        }

        private void WithWorkingLine(Action<WorkingLine, EventMonitor<WorkingLine>> testCase, int worker = 1, bool started = true)
        {
            var wl = CreateWorkingLine(worker);
            var wlMon = new EventMonitor<WorkingLine>(wl);

            AssertState(wl, isDisposed: false, busy: false);
            AssertThreadsState(wl, isDisposed: false, isAlive: false, busy: false);

            if (started)
            {
                wl.Start();
                AssertState(wl, isDisposed: false, busy: false);
                AssertThreadsState(wl, isDisposed: false, isAlive: true, busy: false);
                Assert.IsTrue(wlMon.History.IsEmpty);
            }
            wlMon.ClearHistory();

            testCase(wl, wlMon);

            wlMon.ClearHistory();

            wl.Dispose();
            AssertState(wl, isDisposed: true, busy: false);
            AssertThreadsState(wl, isDisposed: true, isAlive: false, busy: false);

            Assert.IsTrue(wlMon.History.IsEmpty);
        }

        [TestMethod]
        [TestCategory("LifeCycle")]
        public void LifeCycleTest()
            => WithWorkingLine(LifeCycleTestCase, worker: 3);

        private void LifeCycleTestCase(WorkingLine wl, EventMonitor<WorkingLine> wlMon)
        {
            // nothing
        }

        [TestMethod]
        public void SingleWorkerSingleTaskTest()
            => WithWorkingLine(SingleWorkerSingleTaskTestCase, worker: 1, started: false);

        private void SingleWorkerSingleTaskTestCase(WorkingLine wl, EventMonitor<WorkingLine> wlMon)
        {
            var q = wl.Queue;
            var task = new TestTask("single");
            var taskMon = new EventMonitor<TestTask>(task);

            Assert.IsTrue(q.IsEmpty);
            wl.Enqueue(task);
            Assert.IsFalse(q.IsEmpty);
            AssertState(wl, isDisposed: false, busy: false);
            
            wl.Start();
            wl.WaitForEnd(timeout: 10000);

            Assert.IsTrue(q.IsEmpty);
            AssertState(wl, isDisposed: false, busy: false);
            wlMon.History
                .AssertSender(wl)
                .AssertEventNames(
                    nameof(WorkingLine.BusyChanged),
                    nameof(WorkingLine.BusyWorkerCountChanged),
                    nameof(WorkingLine.TaskBegin),
                    nameof(WorkingLine.TaskEnd),
                    nameof(WorkingLine.BusyWorkerCountChanged),
                    nameof(WorkingLine.BusyChanged));
            wlMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkingLine.Busy)))
                .AssertPropertyValues(true, false);
            wlMon.FilterHistory(ByPropertyChanges<int>(nameof(WorkingLine.BusyWorkerCount)))
                .AssertPropertyValues(1, 0);

            taskMon.History.AssertSender(task);
            taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)))
                .AssertPropertyValues(
                    TaskState.InProgress,
                    TaskState.CleaningUp,
                    TaskState.Succeeded);
        }

        [TestMethod]
        public void MultipleWorkerMultipleTaskTest()
            => WithWorkingLine(MultipleWorkerMultipleTaskTestCase, worker: 3, started: true);

        private void MultipleWorkerMultipleTaskTestCase(WorkingLine wl, EventMonitor<WorkingLine> wlMon)
        {
            var q = wl.Queue;
            var tasks = Enumerable.Range(0, 30).Select(i => new TestTask($"multi {i}")).ToArray();
            var taskMons = tasks.Select(t => new EventMonitor<TestTask>(t)).ToArray();

            Assert.IsTrue(q.IsEmpty);
            wl.Enqueue(tasks);
            Thread.Sleep(10);
            AssertState(wl, isDisposed: false, busy: true);

            wl.WaitForEnd(timeout: 10000);

            Assert.IsTrue(q.IsEmpty);
            AssertState(wl, isDisposed: false, busy: false);
            Assert.AreEqual(tasks.Length, wlMon.FilterHistory(ByEventName(nameof(WorkingLine.TaskBegin))).Count);
            Assert.AreEqual(tasks.Length, wlMon.FilterHistory(ByEventName(nameof(WorkingLine.TaskEnd))).Count);
            wlMon.History.AssertSender(wl);
            wlMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkingLine.Busy)))
                .AssertPropertyValues(true, false);
            wlMon.FilterHistory(ByPropertyChanges<int>(nameof(WorkingLine.BusyWorkerCount)))
                .AssertPropertyValueChanges<int>()
                .AssertPropertyValues(1, 2, 3, 2, 1, 0);

            for (int i = 0; i < tasks.Length; i++)
            {
                var task = tasks[i];
                var taskMon = taskMons[i];
                taskMon.History.AssertSender(task);
                taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)))
                    .AssertPropertyValues(
                        TaskState.InProgress,
                        TaskState.CleaningUp,
                        TaskState.Succeeded);
            }
        }
    }
}
