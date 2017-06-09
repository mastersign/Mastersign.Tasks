using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Mastersign.Tasks
{
    public interface ITask : INotifyPropertyChanged
    {
        string Name { get; }

        string Target { get; }

        string Description { get; }

        string QueueTag { get; }

        ITask[] Dependencies { get; }

        float Progress { get; }

        event EventHandler ProgressChanged;

        string ProgressMessage { get; }

        event EventHandler ProgressMessageChanged;

        TaskState State { get; }

        event EventHandler StateChanged;

        string ErrorMessage { get; }

        event EventHandler ErrorMessageChanged;

        Exception Error { get; }

        event EventHandler ErrorChanged;

        void UpdateState(TaskState newState);

        void UpdateState(TaskState newState, Exception error);
    }
}
