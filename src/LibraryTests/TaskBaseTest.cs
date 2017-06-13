using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class TaskBaseTest
    {
        [TestMethod]
        public void ConstructorTest()
        {
            const string QUEUE_TAG = "Test Tag";

            var t = new TaskImplementation(QUEUE_TAG);

            Assert.AreEqual(QUEUE_TAG, t.QueueTag);
            Assert.AreEqual(TaskState.Waiting, t.State);
            Assert.AreEqual(0f, t.Progress);
            Assert.IsNull(t.ProgressMessage);
            Assert.IsNull(t.ErrorMessage);
            Assert.IsNull(t.Error);
        }

        [TestMethod]
        public void StateNotificationTest()
        {
            var t = new TaskImplementation("tag");

            var stateChangedCount = 0;
            var stateChangedSender = default(object);
            var states = new List<TaskState>();

            t.StateChanged += (sender, e) =>
            {
                stateChangedCount++;
                stateChangedSender = sender;
                states.Add(((ITask)sender).State);
            };

            t.State = TaskState.InProgress;
            t.State = TaskState.Succeeded;
            Assert.AreEqual(2, stateChangedCount);
            Assert.AreEqual(t, stateChangedSender);
            CollectionAssert.AreEqual(
                new[] {
                    TaskState.InProgress,
                    TaskState.Succeeded
                },
                states);
        }

        private class TaskImplementation : TaskBase
        {
            public TaskImplementation(string queueTag)
                : base(queueTag)
            {
            }
        }
    }
}
