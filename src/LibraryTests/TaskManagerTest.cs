﻿using System;
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
        private const int DEF_RAND_INIT = 0;
        private const int DEF_TASK_COUNT = 40;
        private const int DEF_TASK_LEVELS = 20;
        private const int DEF_TASK_MIN_DEPS = 1;
        private const int DEF_TASK_MAX_DEPS = 2;

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

        private TaskGraphMonitor InitializeWithTasks(TaskManager tm,
            int randInit = DEF_RAND_INIT,
            int count = DEF_TASK_COUNT, int levels = DEF_TASK_LEVELS,
            int minDeps = DEF_TASK_MIN_DEPS, int maxDeps = DEF_TASK_MAX_DEPS)
        {
            var rand = new Random(randInit);
            var queueTags = (from wl in tm.WorkingLines select wl.QueueTag).ToArray();
            var tasks = TestTaskFactory.CreateMeshedCascade(rand,
                count: count, levels: levels, minDeps: minDeps, maxDeps: maxDeps,
                queueTags: queueTags);
            var tgMon = new TaskGraphMonitor(tasks);
            tm.AddTasks(tasks);
            return tgMon;
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
        [Ignore]
        public void TaskGenerationTest()
        {
            var rand = new Random(1);
            var tasks = TestTaskFactory.CreateMeshedCascade(rand, 50, 8, 1, 3, "A", "B", "C");
            TaskGraphRenderer.DisplayGraph(tasks);
        }

        private void RenderAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            TaskGraphMonitor tgMon, string fileName)
        {
            Console.WriteLine("Rendering the task graph processing animation");
            TaskGraphRenderer.RenderTaskGraphAnimation(tgMon.Tasks, tgMon,
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    fileName + ".avi"),
                maxWidth: 1024, format: TaskGraphRenderer.VideoFormat.AviMjpeg, fps: 3.333f);

            tgMon.AssertTaskEventsRespectDependencies();
        }

        [TestMethod]
        public void MultiTaskTest()
            => WithTaskManager(MultiTaskBeforeStart, MultiTaskAfterFinish,
                Tuple.Create("A", 1), Tuple.Create("B", 3), Tuple.Create("C", 5));

        private TaskGraphMonitor MultiTaskBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var tgMon = InitializeWithTasks(tm);
            AssertState(tm, isDisposed: false, isRunning: false);
            return tgMon;
        }

        private void MultiTaskAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            TaskGraphMonitor tgMon)
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

            Assert.AreEqual(tgMon.Tasks.Count, tmMon.FilterHistory(ByEventName(nameof(TaskManager.TaskBegin))).Count);
            Assert.AreEqual(tgMon.Tasks.Count, tmMon.FilterHistory(ByEventName(nameof(TaskManager.TaskEnd))).Count);

            foreach (var taskMon in tgMon.TaskMonitors)
            {
                taskMon.History.AssertSender(taskMon.Target);
                taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)))
                    .AssertPropertyValues(
                        TaskState.InProgress,
                        TaskState.CleaningUp,
                        TaskState.Succeeded);
            }

            tgMon.AssertTaskEventsRespectDependencies();
        }

        [TestMethod]
        [Ignore]
        public void RenderMultiTaskTest()
            => WithTaskManager(MultiTaskBeforeStart,
                (tm, tmMon, tgMon) => RenderAfterFinish(tm, tmMon, tgMon, "multitask"),
                Tuple.Create("A", 1), Tuple.Create("B", 2), Tuple.Create("C", 3));

        [TestMethod]
        public void SequentialCancellationTest()
            => WithTaskManager(CancellationBeforeStart, CancellationAfterFinish,
                Tuple.Create("A", 1));

        [TestMethod]
        public void ParallelCancellationTest()
            => WithTaskManager(CancellationBeforeStart, CancellationAfterFinish,
                Tuple.Create("A", 4));

        private TaskGraphMonitor CancellationBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var tgMon = InitializeWithTasks(tm);

            // find a task with responsibilities and dependencies
            var cancelTask = TestTaskFactory.TasksWithResponsibilities(tgMon.Tasks).FirstOrDefault();
            Assert.IsNotNull(cancelTask);
            // cancel the task manager as soon as this task gets worked on
            cancelTask.StateChanged += (sender, a) =>
            {
                if (cancelTask.State == TaskState.InProgress) tm.Cancel();
            };

            return tgMon;
        }

        private void CancellationAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            TaskGraphMonitor tgMon)
        {
            Assert.IsFalse(tgMon.Tasks.Where(t => t.State == TaskState.Waiting).Any(),
                "Some tasks are still waiting.");
            Assert.IsFalse(tgMon.Tasks.Where(t => t.State == TaskState.InProgress).Any(),
                "Some tasks are still in progress.");
            Assert.IsFalse(tgMon.Tasks.Where(t => t.State == TaskState.CleaningUp).Any(),
                "Some tasks are still in clean-up state.");
            Assert.IsTrue(tgMon.Tasks.Where(t => t.State == TaskState.Succeeded).Any(),
                "No tasks succeeded.");
            Assert.IsTrue(tgMon.Tasks.Where(t => t.State == TaskState.Canceled).Any(),
                "No task was cancelled.");
            Assert.IsTrue(tgMon.Tasks.Where(t => t.State == TaskState.Obsolete).Any(),
                "No task got obsolete.");
        }

        [TestMethod]
        [Ignore]
        public void RenderCancellationTest()
            => WithTaskManager(CancellationBeforeStart, 
                (tm, tmMon, tgMon) => RenderAfterFinish(tm, tmMon, tgMon, "cancellation"),
            Tuple.Create("A", 1));

    }
}
