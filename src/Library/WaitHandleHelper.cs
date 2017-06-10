using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Mastersign.Tasks
{
    public static class WaitHandleHelper
    {
        public static void Dispose(AutoResetEvent e)
        {
            e.Close();
            // Release all remaining blocked threads
            while (true)
            {
                try
                {
                    e.Set();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }
    }
}
