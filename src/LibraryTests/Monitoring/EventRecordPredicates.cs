using System;

namespace Mastersign.Tasks.Test.Monitors
{
    static class EventRecordPredicates
    {
        public static Func<EventRecord, bool> ByEventName(string eventName)
        {
            return er => string.Equals(er.EventName, eventName);
        }

        public static Func<EventRecord, bool> ByPropertyChanges<T>(string propertyName)
        {
            return er => string.Equals(er.EventName, propertyName + "Changed") &&
                er.ValueUpdate?.GetType() == typeof(ValueUpdate<T>);
        }
    }
}
