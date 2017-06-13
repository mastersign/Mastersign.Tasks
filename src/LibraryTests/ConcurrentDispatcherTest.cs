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
    public class ConcurrentDispatcherTest
    {
        [TestMethod]
        public void InitializationTest()
        {
            using (var q = new ConcurrentDispatcher<string>())
            {
                Assert.IsTrue(q.IsEmpty);
            }
        }

        [TestMethod]
        public void OrderedSequentialTest()
        {
            const int N = 1_000_000;
            const string FORMAT = "0000000000";
            const string INIT = "init";

            var q = new ConcurrentDispatcher<string>();

            // enqueue N items
            for (int i = 0; i < N; i++)
            {
                q.Enqueue(i.ToString(FORMAT));
            }

            Assert.IsFalse(q.IsEmpty);

            // dequeue N items
            for (int i = 0; i < N; i++)
            {
                var s = INIT;
                var returnVal = q.TryDequeue(ref s);
                Assert.IsTrue(returnVal);
                Assert.AreNotEqual(INIT, s);
                Assert.IsNotNull(s);
                Assert.AreEqual(i.ToString(FORMAT), s);
            }

            Assert.IsTrue(q.IsEmpty);

            // try dequeue empty queue

            var s2 = INIT;
            var returnVal2 = q.TryDequeue(ref s2);
            Assert.IsFalse(returnVal2);
            Assert.AreEqual(INIT, s2);

            // dispose dispatcher
            Assert.IsFalse(q.IsDisposed);
            q.Dispose();
            Assert.IsTrue(q.IsDisposed);

            string s3 = null;
            // try non-blocking dequeue
            Assert.IsFalse(q.TryDequeue(ref s3));
            // try blocking dequeue
            Assert.IsFalse(q.Dequeue(ref s3));
        }

        [TestMethod]
        public void ConcurrentTest()
        {
            const int N = 1_000_000;
            const int PRODUCERS = 3;
            const int CONSUMERS = 3;
            const string FORMAT = "0000000000";
            var rand = new Random();

            var q = new ConcurrentDispatcher<string>();

            // prepare producer bags
            var producerItems = new List<string>[PRODUCERS];
            for (int i = 0; i < producerItems.Length; i++)
            {
                producerItems[i] = new List<string>();
            }

            // fill producer bags randomly
            for (int i = 0; i < N; i++)
            {
                producerItems[rand.Next(producerItems.Length)].Add(i.ToString(FORMAT));
            }

            // prepare event check
            var productionCount = 0;
            q.NewItem += (sender, e) => { Interlocked.Increment(ref productionCount); };

            // prepare consumer bags
            var consumerItems = new List<string>[CONSUMERS];
            for (int i = 0; i < consumerItems.Length; i++)
            {
                consumerItems[i] = new List<string>();
            }

            // create producer threads
            var producers = new Thread[PRODUCERS];
            for (int i = 0; i < producers.Length; i++)
            {
                var threadIndex = i;
                producers[threadIndex] = new Thread(() =>
                {
                    foreach (var item in producerItems[threadIndex])
                    {
                        q.Enqueue(item);
                    }
                });
            }

            // create comsumer threads
            var consumers = new Thread[CONSUMERS];
            for (int t = 0; t < consumers.Length; t++)
            {
                var threadIndex = t;
                consumers[threadIndex] = new Thread(() =>
                {
                    string s = null;
                    while (q.Dequeue(ref s))
                    {
                        consumerItems[threadIndex].Add(s);
                    }
                });
            }

            // start threads
            for (int i = 0; i < consumers.Length; i++) consumers[i].Start();
            for (int i = 0; i < producers.Length; i++) producers[i].Start();

            // wait for producer threads to end
            for (int i = 0; i < producers.Length; i++) producers[i].Join();

            // check for production count
            Assert.AreEqual(N, productionCount, "The NewItem event was not fired the right number of times.");

            // wait for consumer threads to block
            for (int i = 0; i < consumers.Length; i++)
            {
                var thread = consumers[i];
                while (thread.ThreadState != ThreadState.WaitSleepJoin)
                {
                    Thread.Sleep(1);
                }
            }

            // dispose the dispatcher
            q.Dispose();

            // wait for consumer threads to end
            for (int i = 0; i < consumers.Length; i++) consumers[i].Join();

            // check completeness in consumer items
            var consumptionIndex = new int[N];
            for (int i = 0; i < consumerItems.Length; i++)
            {
                Assert.IsTrue(consumerItems[i].Count > 0, "At least one consumer did nothing.");

                var last = string.Empty;
                foreach (var item in consumerItems[i])
                {
                    // count occurance
                    var itemNo = int.Parse(item);
                    consumptionIndex[itemNo]++;
                }
            }
            var unconsumedCount = 0;
            var multiConsumptionCount = 0;
            for (int i = 0; i < N; i++)
            {
                if (consumptionIndex[i] == 0) unconsumedCount++;
                if (consumptionIndex[i] > 1) multiConsumptionCount++;
            }
            Assert.AreEqual(0, unconsumedCount, $"{unconsumedCount} items were not consumed");
            Assert.AreEqual(0, multiConsumptionCount, $"{multiConsumptionCount} items were consumed more than once.");
        }
    }
}
