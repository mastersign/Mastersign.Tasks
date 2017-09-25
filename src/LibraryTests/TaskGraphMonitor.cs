using Mastersign.Tasks.Test.Monitors;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    public class TaskGraphMonitor
    {
        private List<EventRecord> EventRecords { get; } = new List<EventRecord>();

        public List<TestTask> Tasks { get; }

        public List<EventMonitor<TestTask>> TaskMonitors { get; }

        public EventHistory History => new EventHistory(EventRecords.ToArray());

        public TaskGraphMonitor(List<TestTask> tasks)
        {
            Tasks = tasks;
            TaskMonitors = tasks.Select(t => new EventMonitor<TestTask>(t)).ToList();
            foreach (var task in tasks)
            {
                task.StateChanging += TaskStateChangingHandler;
            }
        }

        private void TaskStateChangingHandler(object sender, TaskEventArgs e)
        {
            var er = new EventRecord(nameof(TestTask.StateChanging), sender, e);
            lock (EventRecords)
            {
                EventRecords.Add(er);
            }
        }

        #region Assertions

        public void AssertTaskEventsRespectDependencies()
        {
            var states = Tasks.ToDictionary(t => t, t => TaskState.Waiting);
            foreach (var e in EventRecords)
            {
                var ea = e.EventArgs as TaskEventArgs;
                if (e.EventName == nameof(TaskManager.TaskBegin))
                {
                    foreach (TestTask d in ea.Task.Dependencies)
                    {
                        Assert.AreEqual(TaskState.Succeeded, states[d],
                            $"The dependency {((TestTask)d).Label} was not finished before starting {((TestTask)ea.Task).Label}.");
                    }
                    Assert.AreEqual(TaskState.InProgress, ea.State);
                    states[(TestTask)ea.Task] = ea.State;
                }
                if (e.EventName == nameof(TaskManager.TaskEnd))
                {
                    Assert.AreEqual(TaskState.Succeeded, ea.State);
                    states[(TestTask)ea.Task] = ea.State;
                }
            }
        }

        #endregion
    }
}
