using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class CancelationToken
    {
        public event EventHandler Canceled;

        public bool IsCanceled { get; private set; }

        private readonly object lockHandle = new object();

        public void Cancel()
        {
            var notify = false;
            lock(lockHandle)
            {
                if (!IsCanceled)
                {
                    IsCanceled = true;
                    notify = true;
                }
            }
            if (notify)
            {
                Canceled?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
