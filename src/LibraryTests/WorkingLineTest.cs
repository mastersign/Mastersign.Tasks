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
using System.Diagnostics;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class WorkingLineTest
    {
        private readonly Stopwatch _watch = new Stopwatch();

        [TestInitialize]
        public void Initialize()
        {
            TestProcessControl.SetProcessAffinity();
            _watch.Start();
            TaskDebug.Stopwatch = _watch;
        }

        [TestCleanup]
        public void Cleanup()
        {
            _watch.Stop();
        }

        private static WorkingLine CreateWorkingLine(int worker)
        {
            const string TAG = "Test";
            var el = new EventLoop("Test");
            var wf = new TestWorkerFactory();
            var wl = new WorkingLine(el, TAG, wf, worker: worker);
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
            wlMon.History.AssertSender(wl);
            wlMon.FilterHistory(ByEventName(
                    nameof(WorkingLine.TaskBegin),
                    nameof(WorkingLine.TaskEnd)))
                .AssertEventNames(
                    nameof(WorkingLine.TaskBegin),
                    nameof(WorkingLine.TaskEnd));
            wlMon.FilterHistory(ByEventName(
                    nameof(WorkingLine.BusyChanged),
                    nameof(WorkingLine.BusyWorkerCountChanged)))
                .AssertEventNames(
                    nameof(WorkingLine.BusyChanged),
                    nameof(WorkingLine.BusyWorkerCountChanged),
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

        [TestMethod]
        public void SequentialCancellationTest()
            => WithWorkingLine(SequentialCancellationTestCase, worker: 1, started: true);

        private void SequentialCancellationTestCase(WorkingLine wl, EventMonitor<WorkingLine> wlMon)
        {
            var q = wl.Queue;
            var tasks = Enumerable.Range(0, 4).Select(i => new TestTask(i.ToString())).ToArray();

            var cancelTask = tasks[1];
            cancelTask.StateChanging += (s, e) =>
            {
                if (e.NewValue == TaskState.InProgress) wl.Cancel();
            };

            wl.Enqueue(tasks);

            Assert.IsTrue(wl.WaitForEnd(timeout: 4000));

            AssertState(wl, isDisposed: false, busy: false);
            Assert.AreEqual(0, wl.CurrentTasks.Length);
            Assert.IsTrue(wl.Queue.IsEmpty);

            Assert.AreEqual(TaskState.Succeeded, tasks[0].State);
            Assert.AreEqual(TaskState.Canceled, tasks[1].State);
            Assert.AreEqual(TaskState.Obsolete, tasks[2].State);
            Assert.AreEqual(TaskState.Obsolete, tasks[3].State);

            wlMon.History
                .AssertSender(wl)
                .AssertEventNames(
                nameof(WorkingLine.TaskBegin),
                nameof(WorkingLine.BusyChanged),
                nameof(WorkingLine.BusyWorkerCountChanged),
                nameof(WorkingLine.TaskEnd),
                nameof(WorkingLine.TaskBegin),
                nameof(WorkingLine.Cancelled),
                nameof(WorkingLine.TaskEnd),
                nameof(WorkingLine.BusyWorkerCountChanged),
                nameof(WorkingLine.BusyChanged));
        }

        [TestMethod]
        public void ParallelCancellationTest()
            => WithWorkingLine(ParallelCancellationTestCase, worker: 3, started: true);

        private void ParallelCancellationTestCase(WorkingLine wl, EventMonitor<WorkingLine> wlMon)
        {
            var q = wl.Queue;
            var tasks = Enumerable.Range(0, 18).Select(i => new TestTask(i.ToString())).ToArray();
            var taskMons = tasks.Select(t => new EventMonitor<TestTask>(t)).ToArray();

            var cancelTask = tasks[9];
            cancelTask.StateChanging += (s, e) =>
            {
                if (e.NewValue == TaskState.InProgress) wl.Cancel();
            };

            wl.Enqueue(tasks);

            Assert.IsTrue(wl.WaitForEnd(timeout: 4000));

            AssertState(wl, isDisposed: false, busy: false);
            Assert.AreEqual(0, wl.CurrentTasks.Length);
            Assert.IsTrue(wl.Queue.IsEmpty);

            Assert.AreEqual(TaskState.Succeeded, tasks.First().State);
            Assert.AreEqual(TaskState.Obsolete, tasks.Last().State);
            Assert.AreEqual(tasks.Length,
                tasks.Where(t => 
                    t.State == TaskState.Succeeded ||
                    t.State == TaskState.Canceled || 
                    t.State == TaskState.Obsolete)
                .Count());

            wlMon.FilterHistory(ByEventName(
                nameof(WorkingLine.BusyChanged),
                nameof(WorkingLine.Cancelled)))
                .AssertEventNames(
                    nameof(WorkingLine.BusyChanged),
                    nameof(WorkingLine.Cancelled),
                    nameof(WorkingLine.BusyChanged));

            wlMon.FilterHistory(ByEventName(nameof(WorkingLine.BusyChanged)))
                .AssertPropertyValues(true, false);

            wlMon.FilterHistory(ByEventName(nameof(WorkingLine.BusyWorkerCountChanged)))
                .AssertPropertyValues(1, 2, 3, 2, 1, 0);
        }
    }
}
