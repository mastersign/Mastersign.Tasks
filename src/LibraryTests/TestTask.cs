using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    class TestTask : TaskBase
    {
        public string Label { get; private set; }

        public TestTask(string label, string queueTag) 
            : base(queueTag)
        {
            Label = label;
        }
    }
}
