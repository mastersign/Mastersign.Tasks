using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    internal class EventLoop : IDisposable
    {
        private readonly ConcurrentDispatcher<DelegateCall> _queue = new ConcurrentDispatcher<DelegateCall>();
        private readonly Thread _loopThread;

        public event EventHandler<UnhandledExceptionEventArgs> UnhandledException;

        public EventLoop()
        {
            _loopThread = new Thread(Loop);
            _loopThread.Start();
        }

        public void Push(Delegate @delegate, params object[] parameter)
        {
            _queue.Enqueue(new DelegateCall(@delegate, parameter));
        }

        private void Loop()
        {
            DelegateCall action = null;
            while (_queue.Dequeue(ref action))
            {
                try
                {
                    action.Delegate.DynamicInvoke(action.Parameter);
                }
                catch (Exception e)
                {
                    OnUnhandledException(e);
                }
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
            return _queue.WaitForEmpty(timeout);
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _queue.Dispose();
            _loopThread.Join();
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
        }
    }
}
