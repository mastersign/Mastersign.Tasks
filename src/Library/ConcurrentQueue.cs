﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class ConcurrentQueue<T>
    {
        protected readonly Queue<T> items = new Queue<T>();

        public event EventHandler NewItem;

        public event EventHandler Empty;

        public bool IsEmpty
        {
            get
            {
                lock (items)
                {
                    return items.Count == 0;
                }
            }
        }

        public void Enqueue(T item)
        {
            lock (items)
            {
                items.Enqueue(item);
                OnNewItem();
            }
        }

        protected virtual void OnNewItem()
        {
            NewItem?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnEmpty()
        {
            Empty?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Tries to dequeue an item. Does not block the caller.
        /// </summary>
        /// <param name="item">If <c>true</c> is returned, is set to the dequeued item; otherwise remains unchanged.</param>
        /// <returns><c>true</c> if an item could be dequeued; otherwise <c>false</c></returns>
        public bool TryDequeue(ref T item)
        {
            lock (items)
            {
                if (items.Count > 0)
                {
                    item = items.Dequeue();
                    if (items.Count == 0)
                    {
                        OnEmpty();
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public virtual T[] DequeueAll()
        {
            T[] removedItems;
            lock(items)
            {
                removedItems = items.ToArray();
                items.Clear();
            }
            if (removedItems.Length > 0) OnEmpty();
            return removedItems;
        }
    }
}
