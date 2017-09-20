﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test.Monitors
{
    public class EventHistory : ICollection<EventRecord>, ICollection
    {
        public EventRecord[] EventRecords { get; }

        public EventHistory(EventRecord[] records)
        {
            EventRecords = records;
        }

        public T[] PropertyValues<T>()
        {
            return EventRecords.Select(er =>
            {
                var v = er.ValueUpdate?.GetNewValue();
                return v == null ? default(T) : (T)v;
            }).ToArray();
        }

        public EventRecord First => EventRecords.Length > 0 ? EventRecords[0] : null;

        public EventRecord Last => EventRecords.Length > 0 ? EventRecords[EventRecords.Length - 1] : null;

        #region Assertions

        public EventHistory AssertEventNames(params string[] eventNames)
        {
            CollectionAssert.AreEqual(EventRecords.Select(re => re.EventName).ToArray(), eventNames);
            return this;
        }

        public EventHistory AssertPropertyValues<T>(params T[] values)
        {
            CollectionAssert.AreEqual(PropertyValues<T>(), values);
            return this;
        }

        public EventHistory AssertSender(object sender)
        {
            foreach (var re in EventRecords)
            {
                Assert.AreEqual(sender, re.Sender, $"The sender of the {re.EventName} event is unexpected.");
            }
            return this;
        }

        public EventHistory AssertPropertyValueChanges<T>()
        {
            if (EventRecords.Length < 2) return this;
            var value = EventRecords[0].GetNewValue<T>();
            foreach (var re in EventRecords)
            {
                var newValue = re.GetNewValue<T>();
                Assert.AreNotEqual<T>(value, newValue, $"The {re.EventName} event fired, but the value did not change.");
                value = newValue;
            }
            return this;
        }

        #endregion

        #region ICollection Implementation 

        public int Count => EventRecords.Length;

        public object SyncRoot => EventRecords;

        public bool IsSynchronized => true;

        public void CopyTo(Array array, int index) => EventRecords.CopyTo(array, index);

        #endregion

        #region ICollection<EventRecord> Implementation

        public bool Contains(EventRecord item) => EventRecords.Contains(item);

        public void CopyTo(EventRecord[] array, int arrayIndex) => EventRecords.CopyTo(array, arrayIndex);

        public IEnumerator<EventRecord> GetEnumerator() => EventRecords.Where(er => true).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => EventRecords.GetEnumerator();

        public bool IsReadOnly => true;

        #region Not Supported

        public void Add(EventRecord item) => throw new NotSupportedException();

        public bool Remove(EventRecord item) => throw new NotSupportedException();

        public void Clear() => throw new NotSupportedException();

        #endregion

        #endregion
    }
}
