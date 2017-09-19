using System;

namespace Mastersign.Tasks.Test.Monitors
{
    public class EventRecord
    {
        public string EventName { get; }

        public object Sender { get; }

        public EventArgs EventArgs { get; }

        public ValueUpdate ValueUpdate { get; }

        public EventRecord(string eventName, object sender, EventArgs e, ValueUpdate vu = null)
        {
            EventName = eventName;
            Sender = sender;
            EventArgs = e;
            ValueUpdate = vu;
        }

        public T GetNewValue<T>()
        {
            var vu = ValueUpdate as ValueUpdate<T>;
            if (vu == null) throw new InvalidOperationException(
                "This event record has no value update for " + typeof(T).FullName);
            return vu.NewValue;
        }
    }
}
