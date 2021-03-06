﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mastersign.Tasks.Test.Monitors;
using static Mastersign.Tasks.Test.Monitors.EventRecordPredicates;
using System.Diagnostics;

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

            var waitForFinishResult = finishedEvent.WaitOne(10000);
            finishedEvent.Close();
            var waitForEndResult = tm.WaitForEnd(1000);

            afterFinish.Invoke(tm, tmMon, cache);

            tmMon.ClearHistory();

            Assert.IsTrue(waitForEndResult, "WaitForEnd did not finish in time.");
            Assert.IsTrue(waitForFinishResult, "The event TaskManager.Finished did not fire in time.");

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
        public void SingleTaskTest()
            => WithTaskManager(SingleTaskTestBeforeStart, SingleTaskTestAfterFinish, Tuple.Create("A", 1));

        private EventMonitor<TestTask> SingleTaskTestBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var task = new TestTask("single", "A");
            var taskMon = new EventMonitor<TestTask>(task);

            tm.AddTask(task);
            AssertState(tm, isDisposed: false, isRunning: false);

            return taskMon;
        }

        private void SingleTaskTestAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            EventMonitor<TestTask> taskMon)
        {
            AssertState(tm, isDisposed: false, isRunning: false);
            tmMon.FilterHistory(ByEventName(
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.Started),
                    nameof(TaskManager.Finished),
                    nameof(TaskManager.TaskBegin),
                    nameof(TaskManager.TaskEnd)))
                .AssertSender(tm)
                .AssertEventNames(
                    nameof(TaskManager.Started),
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.TaskBegin),
                    nameof(TaskManager.TaskEnd),
                    nameof(TaskManager.IsRunningChanged),
                    nameof(TaskManager.Finished));
            tmMon.FilterHistory(ByPropertyChanges<bool>(nameof(TaskManager.IsRunning)))
                .AssertPropertyValues(true, false);

            taskMon.History.AssertSender(taskMon.Target);
            taskMon.FilterHistory(ByPropertyChanges<TaskState>(nameof(ITask.State)))
                .AssertPropertyValues(
                    TaskState.InProgress,
                    TaskState.CleaningUp,
                    TaskState.Succeeded);
        }

        [TestMethod]
        [TestCategory("Rendering")]
        [Ignore]
        public void RenderDefaultMultiQueueGraphTest()
        {
            var rand = new Random(DEF_RAND_INIT);
            var tasks = TestTaskFactory.CreateMeshedCascade(rand,
                DEF_TASK_COUNT, DEF_TASK_LEVELS, DEF_TASK_MIN_DEPS, DEF_TASK_MAX_DEPS,
                "A", "B", "C");
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
        public void SequentialTaskTest()
            => WithTaskManager(GeneralTasksBeforeStart, GeneralTasksAfterFinish,
                Tuple.Create("A", 1));

        [TestMethod]
        [TestCategory("Concurrent")]
        public void ParallelTaskTest()
            => WithTaskManager(GeneralTasksBeforeStart, GeneralTasksAfterFinish,
                Tuple.Create("A", 4));

        [TestMethod]
        [TestCategory("Concurrent")]
        public void MultiQueueSequentialTaskTest()
            => WithTaskManager(GeneralTasksBeforeStart, GeneralTasksAfterFinish,
                Tuple.Create("A", 1), Tuple.Create("B", 1), Tuple.Create("C", 1));

        [TestMethod]
        [TestCategory("Concurrent")]
        public void MultiQueueParallelTaskTest()
            => WithTaskManager(GeneralTasksBeforeStart, GeneralTasksAfterFinish,
                Tuple.Create("A", 2), Tuple.Create("B", 3), Tuple.Create("C", 4));

        private TaskGraphMonitor GeneralTasksBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var tgMon = InitializeWithTasks(tm);
            AssertState(tm, isDisposed: false, isRunning: false);
            return tgMon;
        }

        private void GeneralTasksAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            TaskGraphMonitor tgMon)
        {
            var queueTags = tm.WorkingLines.Select(wl => wl.QueueTag).ToArray();

            AssertState(tm, isDisposed: false, isRunning: false);
            tmMon.History.AssertSender(tm);

            tmMon.FilterHistory(ByPropertyChanges<bool>(nameof(TaskManager.IsRunning)))
                .AssertPropertyValues(true, false);

            var labels = tgMon.Tasks.Select(t => t.Label).ToList();

            var beginLabels = tmMon.FilterHistory(ByEventName(nameof(TaskManager.TaskBegin)))
                .Select(e => ((TestTask)((TaskEventArgs)e.EventArgs).Task).Label).ToList();
            var unstartedLabels = labels.Except(beginLabels).ToList();
            if (unstartedLabels.Count > 0)
            {
                Debug.WriteLine("Task without begin event: " + string.Join(", ", unstartedLabels));
                Assert.Fail("The following tasks did not fire a begin event: " + string.Join(", ", unstartedLabels));
            }

            var endLabels = tmMon.FilterHistory(ByEventName(nameof(TaskManager.TaskEnd)))
                .Select(e => ((TestTask)((TaskEventArgs)e.EventArgs).Task).Label).ToList();
            var unfinishedLabels = labels.Except(endLabels).ToList();
            if (unfinishedLabels.Count > 0)
            {
                Debug.WriteLine("Task without end event: " + string.Join(", ", unfinishedLabels));
                Assert.Fail("The following tasks did not fire an end event: " + string.Join(", ", unfinishedLabels));
            }

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
        [TestCategory("Rendering")]
        [Ignore]
        public void RenderMultiQueueParallelTaskTest()
            => WithTaskManager(GeneralTasksBeforeStart,
                (tm, tmMon, tgMon) => RenderAfterFinish(tm, tmMon, tgMon, "multitask"),
                Tuple.Create("A", 1), Tuple.Create("B", 2), Tuple.Create("C", 3));

        [TestMethod]
        public void SequentialCancellationTest()
            => WithTaskManager(CancellationBeforeStart, CancellationAfterFinish,
                Tuple.Create("A", 1));

        [TestMethod]
        [TestCategory("Concurrent")]
        public void ParallelCancellationTest()
            => WithTaskManager(CancellationBeforeStart, CancellationAfterFinish,
                Tuple.Create("A", 4));

        [TestMethod]
        [TestCategory("Concurrent")]
        public void MultiQueueSequentialCancellationTest()
            => WithTaskManager(CancellationBeforeStart, CancellationAfterFinish,
                Tuple.Create("A", 1), Tuple.Create("B", 1), Tuple.Create("C", 1));

        [TestMethod]
        [TestCategory("Concurrent")]
        public void MultiQueueParallelCancellationTest()
            => WithTaskManager(CancellationBeforeStart, CancellationAfterFinish,
                Tuple.Create("A", 2), Tuple.Create("B", 3), Tuple.Create("C", 4));

        private TaskGraphMonitor CancellationBeforeStart(TaskManager tm, EventMonitor<TaskManager> tmMon)
        {
            var tgMon = InitializeWithTasks(tm);

            // find a task with responsibilities and dependencies
            var cancelTask = TestTaskFactory.TasksWithResponsibilities(tgMon.Tasks).FirstOrDefault();
            Assert.IsNotNull(cancelTask);
            // cancel the task manager as soon as this task gets worked on
            cancelTask.StateChanged += (sender, ea) =>
            {
                if (ea.NewValue == TaskState.InProgress)
                {
                    Debug.WriteLine("CANCEL");
                    tm.Cancel();
                }
            };

            return tgMon;
        }

        private void CancellationAfterFinish(TaskManager tm, EventMonitor<TaskManager> tmMon,
            TaskGraphMonitor tgMon)
        {
            var labelsByState = new Dictionary<TaskState, List<string>>();
            foreach (TaskState state in Enum.GetValues(typeof(TaskState)))
            {
                var labels = tgMon.Tasks.Where(t => t.State == state).Select(t => t.Label).ToList();
                if (labels.Count > 0)
                {
                    TaskDebug.Verbose($"Tasks with state {state}: {string.Join(", ", labels)}");
                }
                labelsByState[state] = labels;
            }

            Assert.AreEqual(0, labelsByState[TaskState.Waiting].Count,
                "Some tasks are still waiting.");
            Assert.AreEqual(0, labelsByState[TaskState.InProgress].Count,
                "Some tasks are still in progress.");
            Assert.AreEqual(0, labelsByState[TaskState.CleaningUp].Count,
                "Some tasks are still in clean-up state.");
            Assert.AreNotEqual(0, labelsByState[TaskState.Succeeded].Count,
                "No tasks succeeded.");
            Assert.AreNotEqual(0, labelsByState[TaskState.Canceled].Count,
                "No task was cancelled.");
            Assert.AreNotEqual(0, labelsByState[TaskState.Obsolete].Count,
                "No task got obsolete.");
        }

        [TestMethod]
        [TestCategory("Rendering")]
        [Ignore]
        public void RenderSequentialCancellationTest()
            => WithTaskManager(CancellationBeforeStart,
                (tm, tmMon, tgMon) => RenderAfterFinish(tm, tmMon, tgMon, "cancellation"),
            Tuple.Create("A", 1));

    }
}
