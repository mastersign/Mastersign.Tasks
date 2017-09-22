using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mastersign.Tasks.Test.Monitors;
using static Mastersign.Tasks.Test.Monitors.EventRecordPredicates;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class TaskManagerTest
    {
        private void AssertState(TaskManager tm, bool isDisposed, bool isRunning)
        {
            Assert.AreEqual(isDisposed, tm.IsDisposed);
            Assert.AreEqual(isRunning, tm.IsRunning);
        }

        private TaskManager CreateTaskManager(params Tuple<string, int>[] workingLineDescriptions)
        {
            var tm = new TaskManager();
            foreach (var wld in workingLineDescriptions)
            {
                tm.AddWorkingLine(wld.Item1, new TestWorkerFactory(), wld.Item2, ThreadPriority.Normal);
            }
            AssertState(tm, isDisposed: false, isRunning: false);
            Assert.AreEqual(workingLineDescriptions.Length, tm.WorkingLines.Count);
            return tm;
        }

        private void WithTaskManager<T>(
            Func<TaskManager, EventMonitor<TaskManager>, T> beforeStart,
            Action<TaskManager, EventMonitor<TaskManager>, T> afterFinish,
            params Tuple<string, int>[] workingLineDescriptions)
        {
            var tm = CreateTaskManager(workingLineDescriptions);
            var tmMon = new EventMonitor<TaskManager>(tm);

            var finishedEvent = new ManualResetEvent(false);
            tm.Finished += (sender, e) => finishedEvent.Set();

            T cache = (T)beforeStart.Invoke(tm, tmMon);

            tmMon.ClearHistory();

            tm.Start();

            Assert.IsTrue(tm.WaitForEnd(10000));
            Thread.Sleep(100);
            Assert.IsTrue(finishedEvent.WaitOne(1000));
            finishedEvent.Close();


            afterFinish.Invoke(tm, tmMon, cache);

            tmMon.ClearHistory();

            Assert.IsFalse(tm.IsDisposed);
            tm.Dispose();
            AssertState(tm, isDisposed: true, isRunning: false);
            tmMon.History
                .AssertSender(tm)
                .AssertEventNames(nameof(TaskManager.Disposed));
        }

        [TestMethod]
        [TestCategory("LifeCycle")]
        public void LifeCycleTest()
            => WithTaskManager(LifeCycleTestBeforeStart, LifeCycleTestAfterFinish, Tuple.Create("A", 1));

        private object LifeCycleTestBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            AssertState(tm, isDisposed: false, isRunning: false);
            Assert.AreEqual(0, tm.TaskCount);
            return null;
        }

        private void LifeCycleTestAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon, object cache)
        {
            AssertState(tm, isDisposed: false, isRunning: false);
            tmMon.History
                .AssertSender(tm)
                .AssertEventNames(
                    nameof(TaskManager.Started),
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.Finished));
            tmMon.FilterHistory(ByPropertyChanges<bool>(nameof(TaskManager.IsRunning)))
                .AssertPropertyValues(true, false);
        }

        [TestMethod]
        public void MinimalTaskTest()
            => WithTaskManager(MinimalTaskTestBeforeStart, MinimalTaskTestAfterFinish, Tuple.Create("A", 1));

        private EventMonitor<TestTask> MinimalTaskTestBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var task = new TestTask("single", "A");
            var taskMon = new EventMonitor<TestTask>(task);

            tm.AddTask(task);
            AssertState(tm, isDisposed: false, isRunning: false);

            return taskMon;
        }

        private void MinimalTaskTestAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            EventMonitor<TestTask> taskMon)
        {
            AssertState(tm, isDisposed: false, isRunning: false);
            tmMon.History
                .AssertSender(tm)
                .AssertEventNames(
                    nameof(TaskManager.Started),
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.BusyWorkingLinesCountChanged),
                    nameof(TaskManager.TaskBegin),
                    nameof(TaskManager.TaskEnd),
                    nameof(TaskManager.BusyWorkingLinesCountChanged),
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.Finished));
            tmMon.FilterHistory(ByPropertyChanges<bool>(nameof(TaskManager.IsRunning)))
                .AssertPropertyValues(true, false);
            tmMon.FilterHistory(ByPropertyChanges<int>(nameof(TaskManager.BusyWorkingLinesCount)))
                .AssertPropertyValues(1, 0);

            taskMon.History.AssertSender(taskMon.Target);
            taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)))
                .AssertPropertyValues(
                    TaskState.InProgress,
                    TaskState.CleaningUp,
                    TaskState.Succeeded);
        }

        [TestMethod]
        //[Ignore]
        public void TaskGenerationTest()
        {
            var rand = new Random(1);
            var tasks = TestTaskFactory.CreateMeshedCascade(rand, 50, 8, 1, 3, "A", "B", "C");
            TaskGraphRenderer.DisplayGraph(tasks);
        }

        [TestMethod]
        public void MultiTaskTest()
            => WithTaskManager(MultiTaskBeforeStart, MultiTaskAfterFinish,
                Tuple.Create("A", 1), Tuple.Create("B", 3), Tuple.Create("C", 5));

        private EventMonitor<ITask>[] MultiTaskBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var rand = new Random(10);
            var queueTags = new[] { "A", "B", "C" };
            var tasks = TestTaskFactory.CreateMeshedCascade(rand,
                count: 50, levels: 10, minDeps: 1, maxDeps: 3,
                queueTags: queueTags);
            var taskMons = tasks.Select(t => new EventMonitor<ITask>(t)).ToArray();

            tm.AddTasks(tasks);
            AssertState(tm, isDisposed: false, isRunning: false);
            return taskMons;
        }

        private void MultiTaskAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon, 
            EventMonitor<ITask>[] taskMons)
        {
            var queueTags = new[] { "A", "B", "C" };

            AssertState(tm, isDisposed: false, isRunning: false);
            tmMon.History.AssertSender(tm);

            tmMon.FilterHistory(ByPropertyChanges<bool>(nameof(TaskManager.IsRunning)))
                .AssertPropertyValues(true, false);

            var possibleWorkerLineBusyCounts = Enumerable.Range(0, queueTags.Length + 1).ToArray();
            tmMon.FilterHistory(ByPropertyChanges<int>(nameof(TaskManager.BusyWorkingLinesCount)))
                .AssertPropertyValuesInSet(possibleWorkerLineBusyCounts)
                .AssertPropertyValuesOccured(possibleWorkerLineBusyCounts);

            Assert.AreEqual(taskMons.Length, tmMon.FilterHistory(ByEventName(nameof(TaskManager.TaskBegin))).Count);
            Assert.AreEqual(taskMons.Length, tmMon.FilterHistory(ByEventName(nameof(TaskManager.TaskEnd))).Count);

            foreach (var taskMon in taskMons)
            {
                taskMon.History.AssertSender(taskMon.Target);
                taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)))
                    .AssertPropertyValues(
                        TaskState.InProgress,
                        TaskState.CleaningUp,
                        TaskState.Succeeded);
            }
        }

        [TestMethod]
        public void RenderProcessingTest()
            => WithTaskManager(RenderProcessingBeforeStart, RenderProcessingAfterFinish,
        Tuple.Create("A", 1), Tuple.Create("B", 2), Tuple.Create("C", 3));

        private EventMonitor<ITask>[] RenderProcessingBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var rand = new Random(0);
            var queueTags = new[] { "A", "B", "C" };
            var tasks = TestTaskFactory.CreateMeshedCascade(rand,
                count: 40, levels: 20, minDeps: 1, maxDeps: 2,
                queueTags: queueTags);
            var taskMons = tasks.Select(t => new EventMonitor<ITask>(t)).ToArray();

            Console.WriteLine("Rendering the task graph image");
            TaskGraphRenderer.DisplayGraph(tasks);

            tm.AddTasks(tasks);
            return taskMons;
        }

        private void RenderProcessingAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            EventMonitor<ITask>[] taskMons)
        {
            Console.WriteLine("Rendering the task graph processing animation");
            TaskGraphRenderer.RenderTaskGraphAnimation(taskMons.Select(m => (TestTask)m.Target).ToList(), tmMon,
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "task_graph_animation.avi"),
                maxWidth: 1024, format: TaskGraphRenderer.VideoFormat.AviMjpeg);
        }

    }
}
