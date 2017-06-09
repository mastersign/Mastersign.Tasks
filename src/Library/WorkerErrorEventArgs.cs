using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class WorkerErrorEventArgs : EventArgs
    {
        public Exception Error { get; private set; }

        public WorkerErrorEventArgs(Exception error)
        {
            Error = error ?? throw new ArgumentNullException();
        }
    }
}
