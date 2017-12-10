using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    [TestClass]
    public class EventLoopTest
    {
        [TestInitialize]
        public void Initialize()
        {
            TestProcessControl.SetProcessAffinity();
        }

        [TestMethod]
        public void ConcurrentOrderTest()
        {
            var eventLoop = new EventLoop("Test");

            var sourceItems = Enumerable.Range(0, 10000).ToArray();
            var threadCount = 20;
            var resultLists = Enumerable.Range(0, threadCount).Select(i => new List<int>()).ToArray();
            var threadEvents = resultLists.Select(rl =>
            {
                var endEvent = new ManualResetEventSlim(false);
                var t = new Thread(() =>
                {
                    var rand = new Random();
                    foreach (var value in sourceItems) {
                        var v = value;
                        eventLoop.Push((Action)(() => { rl.Add(v); }));
                        if (rand.NextDouble() > 0.9) Thread.Sleep(1);
                    }
                    endEvent.Set();
                });
                t.Start();
                return endEvent;
            }).ToArray();
            threadEvents.All(te => { te.Wait(); te.Dispose(); return true; });
            eventLoop.WaitForEmpty();

            foreach (var rl in resultLists)
            {
                CollectionAssert.AreEqual(sourceItems, rl);
            }
        }
    }
}
