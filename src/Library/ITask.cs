using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Mastersign.Tasks
{
    public interface ITask : INotifyPropertyChanged
    {
        string QueueTag { get; }

        ITask[] Dependencies { get; }

        bool HasDependencies { get; }

        float Progress { get; }

        event EventHandler<PropertyUpdateEventArgs<float>> ProgressChanged;

        string ProgressMessage { get; }

        event EventHandler<PropertyUpdateEventArgs<string>> ProgressMessageChanged;

        TaskState State { get; }

        event EventHandler<PropertyUpdateEventArgs<TaskState>> StateChanged;

        string ErrorMessage { get; }

        event EventHandler<PropertyUpdateEventArgs<string>> ErrorMessageChanged;

        Exception Error { get; }

        event EventHandler<PropertyUpdateEventArgs<Exception>> ErrorChanged;

        void UpdateState(TaskState newState);

        void UpdateState(TaskState newState, Exception error);
    }
}
