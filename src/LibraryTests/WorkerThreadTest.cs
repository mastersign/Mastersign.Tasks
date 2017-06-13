using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;

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

            Assert.IsTrue(wt.Name.EndsWith(NAME));
            Assert.IsFalse(wt.IsDisposed);
            Assert.IsFalse(wt.IsAlive);

            var isAliveEventCounter = 0;
            var isAliveEventSender = default(object);
            wt.IsAliveChanged += (sender, e) =>
            {
                isAliveEventCounter++;
                isAliveEventSender = sender;
            };

            wt.Start();
            Assert.IsTrue(wt.IsAlive);
            Assert.AreEqual(1, isAliveEventCounter);
            Assert.AreEqual(wt, isAliveEventSender);

            q.Dispose();
            wt.Dispose();
            Assert.IsTrue(wt.IsDisposed);
            Assert.AreEqual(2, isAliveEventCounter);
            Assert.AreEqual(wt, isAliveEventSender);
            Assert.IsFalse(wt.IsAlive);
        }

        [TestMethod]
        public void SingleTaskTest()
        {
            var q = new TaskQueue();
            var w = new TestWorker("Test");
            var wt = new WorkerThread(q, w, "Test");

            var taskBeginCounter = 0;
            var taskBeginSender = default(object);
            var taskEndCounter = 0;
            var taskEndSender = default(object);
            var busyChangedCounter = 0;
            var busyChangedSender = default(object);

            wt.TaskBegin += (sender, e) => {
                taskBeginCounter++;
                taskBeginSender = sender;
            };
            wt.TaskEnd += (sender, e) =>
            {
                taskEndCounter++;
                taskEndSender = sender;
            };
            wt.BusyChanged += (sender, e) =>
            {
                busyChangedCounter++;
                busyChangedSender = sender;
            };

            wt.Start();
            Assert.AreEqual(0, taskBeginCounter);
            Assert.AreEqual(0, taskEndCounter);
            Assert.AreEqual(0, busyChangedCounter);

            var task = new TestTask("single", "test");
            var taskStates = new List<TaskState>();
            task.StateChanged += (sender, e) =>
            {
                taskStates.Add(((ITask)sender).State);
            };

            q.Enqueue(task);

            while (taskEndCounter == 0)
            {
                Thread.Sleep(10);
            }

            Assert.IsTrue(q.IsEmpty);
            Assert.AreEqual(1, taskBeginCounter);
            Assert.AreEqual(wt, taskBeginSender);
            Assert.AreEqual(1, taskEndCounter);
            Assert.AreEqual(wt, taskEndSender);
            Assert.AreEqual(2, busyChangedCounter);
            Assert.AreEqual(wt, busyChangedSender);

            CollectionAssert.AreEqual(new[]
            {
                TaskState.InProgress,
                TaskState.CleaningUp,
                TaskState.Succeeded,
            }, taskStates);
            Assert.AreEqual(TaskState.Succeeded, task.State);

            q.Dispose();
            wt.Dispose();
        }
    }
}
