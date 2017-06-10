using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public class UnhandledExceptionEventArgs : EventArgs
    {
        public Exception Exception { get; private set; }

        public UnhandledExceptionEventArgs(Exception exception)
        {
            Exception = exception ?? throw new ArgumentNullException();
        }
    }
}
