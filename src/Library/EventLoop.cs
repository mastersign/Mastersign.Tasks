using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    internal class EventLoop : IDisposable
    {
        private ConcurrentDispatcher<ActionHandle> _queue = new ConcurrentDispatcher<ActionHandle>();
        private Thread _loopThread;

        public event EventHandler<UnhandledExceptionEventArgs> UnhandledException;

        public EventLoop()
        {
            _loopThread = new Thread(Loop);
            _loopThread.Start();
        }

        private void Loop()
        {
            ActionHandle action = null;
            while (_queue.Dequeue(ref action))
            {
                try
                {
                    action?.Invoke();
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

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _queue.Dispose();
            _loopThread.Join();
        }
    }
}
