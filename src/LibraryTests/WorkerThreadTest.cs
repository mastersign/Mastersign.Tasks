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
        [TestMethod]
        public void LifeCycleTest()
        {
            const string NAME = "TestABC123";
            var q = new TaskQueue();
            var w = new TestWorker("Test");
            var wt = new WorkerThread(q, w, NAME);
            var wtMon = new EventMonitor<WorkerThread>(wt);

            Assert.IsTrue(wt.Name.EndsWith(NAME));
            Assert.IsFalse(wt.IsDisposed);
            Assert.IsFalse(wt.IsAlive);
            Assert.IsFalse(wt.Busy);

            wt.Start();
            Assert.IsTrue(wt.IsAlive);
            var isAliveHistory = wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)));
            isAliveHistory.AssertSender(wt);
            isAliveHistory.AssertPropertyValues(true);

            q.Dispose();
            wt.Dispose();
            Assert.IsTrue(wt.IsDisposed);
            isAliveHistory = wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)));
            isAliveHistory.AssertSender(wt);
            isAliveHistory.AssertPropertyValues(true, false);
            Assert.IsFalse(wt.IsAlive);
            Assert.IsFalse(wt.Busy);
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
            Assert.IsFalse(wt.IsAlive);
            Assert.IsFalse(wt.Busy);

            wt.Start();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(true);

            //WaitFor(() => task.State == TaskState.Succeeded, 1000);
            wt.WaitForEnd(timeout: 10000);
            
            Assert.IsTrue(q.IsEmpty);
            var wtHist = wtMon.History;
            wtHist.AssertSender(wt);
            wtHist.AssertEventNames(
                nameof(wt.IsAliveChanged),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.BusyChanged),
                nameof(wt.TaskBegin),
                nameof(wt.TaskEnd),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.BusyChanged));
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(true);
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.Busy)))
                .AssertPropertyValues(true, false);
            wtMon.FilterHistory(ByPropertyChanges<ITask>(nameof(wt.CurrentTask)))
                .AssertPropertyValues(task, null);

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

            wtMon.ClearHistory();
            q.Dispose();
            wt.Dispose();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(false);
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
            Assert.IsFalse(wt.IsAlive);
            Assert.IsFalse(wt.Busy);

            wt.Start();
            wtMon.FilterHistory(ByPropertyChanges<bool>(nameof(wt.IsAlive)))
                .AssertPropertyValues(true);

            foreach (var task in tasks)
            {
                q.Enqueue(task);
            }

            //WaitFor(() => tasks.All(t => t.State == TaskState.Succeeded), 10000);
            wt.WaitForEnd(timeout: 10000);

            Assert.IsTrue(q.IsEmpty);
            var wtHist = wtMon.History;
            wtHist.AssertSender(wt);
            wtHist.AssertEventNames(
                nameof(wt.IsAliveChanged),
                nameof(wt.CurrentTaskChanged),
                nameof(wt.BusyChanged),
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
