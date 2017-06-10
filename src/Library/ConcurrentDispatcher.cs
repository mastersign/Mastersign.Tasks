using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class ConcurrentDispatcher<T> : ConcurrentQueue<T>, IDisposable
    {
        private readonly AutoResetEvent _newItemEvent = new AutoResetEvent(false);

        protected override void OnNewItem()
        {
            base.OnNewItem();
            _newItemEvent.Set();
        }

        /// <summary>
        /// Waits until the posibillity exists, that <see cref="TryDequeue"/> could be successful.
        /// </summary>
        public void WaitForNewItem()
        {
            lock (items)
            {
                if (items.Count > 0) return;
            }
            try
            {
                _newItemEvent.WaitOne();
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Waits for a successful dequeue or disposal of the dispatcher.
        /// </summary>
        /// <param name="item"></param>
        /// <returns><c>true</c> if <paramref name="item"/> was set to a dequeued item;
        /// <c>false</c> if the dispatcher was disposed.</returns>
        public bool Dequeue(ref T item)
        {
            while (!IsDisposed)
            {
                if (TryDequeue(ref item)) return true;
                WaitForNewItem();
            }
            return false;
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            WaitHandleHelper.Dispose(_newItemEvent);
            items.Clear();
        }
    }
}