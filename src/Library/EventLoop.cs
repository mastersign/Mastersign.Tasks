using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public class EventLoop : IDisposable
    {
        private readonly ConcurrentDispatcher<DelegateCall> _queue = new ConcurrentDispatcher<DelegateCall>();
        private readonly Thread _loopThread;
        private readonly ManualResetEvent _busyEvent = new ManualResetEvent(true);

        public event EventHandler<UnhandledExceptionEventArgs> UnhandledException;

        public EventLoop()
        {
            _loopThread = new Thread(Loop);
            _loopThread.Name = "Event Loop";
            _loopThread.Start();
        }

        public void Push(Delegate @delegate, params object[] parameter)
        {
            var call = new DelegateCall(@delegate, parameter);
            TaskDebug.Verbose($"EventLoop Queue {call}");
            _queue.Enqueue(call);
        }

        private void Loop()
        {
            DelegateCall action = null;
            while (_queue.Dequeue(ref action))
            {
                TaskDebug.Verbose($"EventLoop Dequeue {action}");
                _busyEvent.Reset();
                try
                {
                    action.Delegate.DynamicInvoke(action.Parameter);
                }
                catch (Exception e)
                {
                    OnUnhandledException(e);
                }
                _busyEvent.Set();
            }
        }

        private void OnUnhandledException(Exception e)
        {
            try
            {
                UnhandledException?.Invoke(this, new UnhandledExceptionEventArgs(e));
            }
            catch (Exception)
            {
                // ignore exceptions during event handling for unhandled exceptions
            }
        }

        public bool WaitForEmpty(int timeout = Timeout.Infinite)
        {
            try
            {
                return _queue.WaitForEmpty(timeout) && _busyEvent.WaitOne(timeout);
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _queue.Dispose();
            _loopThread.Join();
            _busyEvent.Close();
        }

        private class DelegateCall
        {
            public Delegate Delegate { get; private set; }

            public object[] Parameter { get; private set; }

            public DelegateCall(Delegate @delegate, params object[] parameter)
            {
                Delegate = @delegate;
                Parameter = parameter;
            }

            public override string ToString()
                => "Delegate " + GetHashCode();
        }
    }
}
