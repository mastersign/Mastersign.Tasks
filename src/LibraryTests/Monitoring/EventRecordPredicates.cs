using System;
using System.Linq;

namespace Mastersign.Tasks.Test.Monitors
{
    static class EventRecordPredicates
    {
        public static Func<EventRecord, bool> ByEventName(params string[] eventNames)
        {
            return er => eventNames.Contains(er.EventName);
        }

        public static Func<EventRecord, bool> ByPropertyChanges<T>(string propertyName)
        {
            return er => string.Equals(er.EventName, propertyName + "Changed") &&
                er.ValueUpdate?.GetType() == typeof(ValueUpdate<T>);
        }
    }
}
