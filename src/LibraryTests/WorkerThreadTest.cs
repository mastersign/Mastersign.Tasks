using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mastersign.Tasks.Test.Monitors;
using static Mastersign.Tasks.Test.Monitors.EventRecordPredicates;
using static Mastersign.Tasks.Test.Waiter;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class WorkerThreadTest
    {
        private void AssertState(WorkerThread wt,
            bool isDisposed, bool isAlive, bool busy)
        {
            Assert.AreEqual(isDisposed, wt.IsDisposed, $"The disposed state of the {nameof(WorkerThread)} is unexpected.");
            Assert.AreEqual(isAlive, wt.IsAlive, $"The alive state of the {nameof(WorkerThread)} is unexpected.");
            Assert.AreEqual(busy, wt.Busy, $"The busy state of the {nameof(WorkerThread)} is unexpected.");
        }

        private void AssertTaskProgressRange(EventMonitor<TestTask> taskMonitor, float first = 0f, float last = 1f)
        {
            var progressHistory = taskMonitor.FilterHistory(ByPropertyChanges<float>(nameof(TestTask.Progress)));
            Assert.IsTrue(progressHistory.Count > 0, "No progress events fired.");
            Assert.AreEqual(first, progressHistory.First.GetNewValue<float>(), $"The first progress event did not signal {first}.");
            Assert.AreEqual(last, progressHistory.Last.GetNewValue<float>(), $"The last progress event did not signal {last}.");
        }

        [TestMethod]
        public void LifeCycleTest()
        {
            const string NAME = "TestABC123";
            var q = new TaskQueue();
            var w = new TestWorker("Test");
            var wt = new WorkerThread(q, w, NAME);
            var wtMon = new EventMonitor<WorkerThread>(wt);

            Assert.IsTrue(wt.Name.EndsWith(NAME));
            AssertState(wt, isDisposed: false, isAlive: false, busy: false);

            wt.Start();
            AssertState(wt, isDisposed: false, isAlive: true, busy: false);
            wtMon.History.AssertSender(wt);
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                .AssertPropertyValues(true);

            q.Dispose();
            wt.Dispose();
            AssertState(wt, isDisposed: true, isAlive: false, busy: false);
            wtMon.History.AssertSender(wt);
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                .AssertPropertyValues(true, false);
        }

        [TestMethod]
        public void SingleTaskTest()
        {
            var q = new TaskQueue();
            var w = new TestWorker("Test");
            var wt = new WorkerThread(q, w, "Test");
            var wtMon = new EventMonitor<WorkerThread>(wt);

            var task = new TestTask("single", "test");
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

            wtMon.ClearHistory();
            q.Dispose();
            wt.Dispose();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(WorkerThread.IsAlive)))
                .AssertPropertyValues(false);

            AssertState(wt, isDisposed: true, isAlive: false, busy: false);
        }

        [TestMethod]
        public void MultiTaskTest()
        {
            var q = new TaskQueue();
            var w = new TestWorker("Test");
            var wt = new WorkerThread(q, w, "Test");
            var wtMon = new EventMonitor<WorkerThread>(wt);

            var tasks = new[] {
                new TestTask("multi 1", "test"),
                new TestTask("multi 2", "test"),
                new TestTask("multi 3", "test")
            };
            var taskMons = tasks.Select(t => new EventMonitor<TestTask>(t)).ToArray();

            Assert.IsTrue(q.IsEmpty);
            AssertState(wt, isDisposed: false, isAlive: false, busy: false);

            wt.Start();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(true);
            AssertState(wt, isDisposed: false, isAlive: true, busy: false);

            foreach (var task in tasks)
            {
                q.Enqueue(task);
            }

            //WaitFor(() => tasks.All(t => t.State == TaskState.Succeeded), 10000);
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
                nameof(wt.TaskBegin),
                nameof(wt.TaskEnd),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.TaskBegin),
                nameof(wt.TaskEnd),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.BusyChanged));
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(true);
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.Busy)))
                .AssertPropertyValues(true, false);
            wtMon.FilterHistory(ByPropertyChanges<ITask>(nameof(wt.CurrentTask)))
                .AssertPropertyValues(tasks.Concat(new TestTask[] { null }).ToArray());

            for (int i = 0; i < tasks.Length; i++)
            {
                var taskMon = taskMons[i];
                var task = tasks[i];
                var taskHist = taskMon.History;
                taskHist.AssertSender(task);
                var taskStateHist = taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(task.State)));
                taskStateHist.AssertPropertyValues(
                    TaskState.InProgress,
                    TaskState.CleaningUp,
                    TaskState.Succeeded
                );
                var taskProgressHist = taskMon.FilterHistory(ByPropertyChanges<float>(nameof(task.Progress)));
                Assert.IsTrue(taskProgressHist.Count > 0);
                Assert.AreEqual(0f, taskProgressHist.First.GetNewValue<float>());
                Assert.AreEqual(1f, taskProgressHist.Last.GetNewValue<float>());
            }

            wtMon.ClearHistory();
            q.Dispose();
            wt.Dispose();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(false);
        }
    }
}
