using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Mastersign.Tasks.Test.Monitors
{
    public class EventMonitor<T>
    {
        public T Target { get; }

        private List<EventRecord> RecordedEvents { get; } = new List<EventRecord>();
        private readonly object recordLock = new object();

        public EventMonitor(T target)
        {
            Target = target;
            ListenToEvents();
        }

        public void ClearHistory()
        {
            lock (recordLock)
            {
                RecordedEvents.Clear();
            }
        }

        private void ListenToEvents()
        {
            var type = typeof(T);
            var events = type.GetEvents(BindingFlags.Instance | BindingFlags.Public);
            foreach (var e in events)
            {
                var eht = e.EventHandlerType;
                if (eht == typeof(EventHandler))
                {
                    if (e.Name.EndsWith("Changed"))
                    {
                        var pName = e.Name.Substring(0, e.Name.Length - "Changed".Length);
                        PropertyInfo p = null;
                        try
                        {
                            p = type.GetProperty(pName, BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance);
                        }
                        catch (Exception) { }
                        e.AddEventHandler(Target, (EventHandler)((sender, ea) =>
                        {
                            ValueUpdate vu = null;
                            if (p != null && p.CanRead)
                            {
                                var newValue = p.GetValue(Target);
                                var vuType = typeof(ValueUpdate<>).MakeGenericType(p.PropertyType);
                                vu = (ValueUpdate)Activator.CreateInstance(vuType, newValue);
                            }
                            EventHandler(e.Name, sender, ea, vu);
                        }));
                        Debug.WriteLine($"EventMonitor<{typeof(T).FullName}>: listening to property changes on {e.Name}");
                    }
                    else
                    {
                        e.AddEventHandler(Target, (EventHandler)((sender, ea) => EventHandler(e.Name, sender, ea)));
                        Debug.WriteLine($"EventMonitor<{typeof(T).FullName}>: listening to {e.Name}");
                    }
                }
                else if (eht.IsGenericType && eht.GetGenericTypeDefinition() == typeof(EventHandler<>))
                {
                    var handlerTarget = new GenericEventHandlerTarget(this, e);
                    e.AddEventHandler(Target, handlerTarget.GetHandlerDelegate());
                    Debug.WriteLine($"EventMonitor<{typeof(T).FullName}>: listening to {e.Name}");
                }
                else
                {
                    Debug.WriteLine($"EventMonitor<{typeof(T).FullName}>: NOT listening to {e.Name}");
                }
            }
        }

        public class GenericEventHandlerTarget
        {
            private readonly EventMonitor<T> eventMonitor;
            private readonly EventInfo eventInfo;

            public GenericEventHandlerTarget(EventMonitor<T> eventMonitor, EventInfo eventInfo)
            {
                this.eventMonitor = eventMonitor;
                this.eventInfo = eventInfo;
            }

            public void Handler<TEventArgs>(object sender, TEventArgs ea) where TEventArgs : EventArgs
            {
                eventMonitor.EventHandler(eventInfo.Name, sender, ea);
            }

            public Delegate GetHandlerDelegate()
            {
                var eht = eventInfo.EventHandlerType;
                var eventArgsType = eht.GetGenericArguments()[0];
                var genericMethodInfo = GetType().GetMethod(nameof(Handler));
                var methodInfo = genericMethodInfo.MakeGenericMethod(eventArgsType);
                return Delegate.CreateDelegate(eht, this, methodInfo);
            }
        }

        private void EventHandler(string eventName, object sender, EventArgs e, ValueUpdate vu = null)
        {
            lock (recordLock)
            {
                // Debug.WriteLine($"EventMonitor<{typeof(T).FullName}>: caught event from {eventName}");
                RecordedEvents.Add(new EventRecord(eventName, sender, e, vu));
            }
        }

        public EventHistory History
        {
            get
            {
                lock (recordLock)
                {
                    return new EventHistory(RecordedEvents.ToArray());
                }
            }
        }

        public EventHistory FilterHistory(params Func<EventRecord, bool>[] predicates)
        {
            lock (recordLock)
            {
                var filteredRecords = predicates.Aggregate(
                    (IEnumerable<EventRecord>)RecordedEvents,
                    (re, predicate) => re.Where(predicate));
                return new EventHistory(filteredRecords.ToArray());
            }
        }
    }
}
