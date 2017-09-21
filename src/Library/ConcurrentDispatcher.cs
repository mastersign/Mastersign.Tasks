using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class ConcurrentDispatcher<T> : ConcurrentQueue<T>, IDisposable
    {
        private readonly AutoResetEvent _newItemEvent = new AutoResetEvent(false);
        private readonly ManualResetEvent _emptyEvent = new ManualResetEvent(true);

        protected override void OnNewItem()
        {
            _emptyEvent.Reset();
            base.OnNewItem();
            _newItemEvent.Set();
        }

        protected override void OnEmpty()
        {
            base.OnEmpty();
            _emptyEvent.Set();
        }

        /// <summary>
        /// Waits until the posibillity exists, that <see cref="TryDequeue"/> could be successful.
        /// </summary>
        /// <param name="timeout">The number of milliseconds to wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><c>true</c> if a new item was queued; <c>false</c> if the wait ran into timeout or the instance is disposed.</returns>
        public bool WaitForNewItem(int timeout = Timeout.Infinite)
        {
            lock (items)
            {
                if (items.Count > 0) return true;
            }
            try
            {
                return _newItemEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Waits until the queue was empty.
        /// </summary>
        /// <param name="timeout">The number of milliseconds to wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><c>true</c> if the queue was empty; <c>false</c> if the wait ran into timeout.</returns>
        public bool WaitForEmpty(int timeout = Timeout.Infinite)
        {
            try
            {
                return _emptyEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        /// <summary>
        /// Waits for a successful dequeue or disposal of the dispatcher.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="timeout">The number of milliseconds to wait before a retry, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><c>true</c> if <paramref name="item"/> was set to a dequeued item;
        /// <c>false</c> if the dispatcher was disposed.</returns>
        public bool Dequeue(ref T item, int timeout = Timeout.Infinite)
        {
            while (!IsDisposed)
            {
                if (TryDequeue(ref item)) return true;
                WaitForNewItem(timeout);
            }
            return false;
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            WaitHandleHelper.Dispose(_newItemEvent);
            _emptyEvent.Close();
            items.Clear();
        }
    }
}