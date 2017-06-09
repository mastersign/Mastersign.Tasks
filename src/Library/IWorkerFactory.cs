using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public interface IWorkerFactory
    {
        IWorker Create();
    }
}
