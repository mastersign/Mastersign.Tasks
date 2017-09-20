using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    class TestWorkerFactory : IWorkerFactory
    {
        public IWorker Create() => new TestWorker();
    }
}
