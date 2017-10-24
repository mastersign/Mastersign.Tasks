using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Mastersign.Tasks
{
    public interface ITask
    {
        string QueueTag { get; }

        ITask[] Dependencies { get; }

        bool HasDependencies { get; }

        float Progress { get; }

        /// <remarks>This event is fired on a worker thread.</remarks>
        event EventHandler<PropertyUpdateEventArgs<float>> ProgressChanged;

        string ProgressMessage { get; }

        /// <remarks>This event is fired on a worker thread.</remarks>
        event EventHandler<PropertyUpdateEventArgs<string>> ProgressMessageChanged;

        TaskState State { get; }

        /// <remarks>This event can fire on any thread.</remarks>
        event EventHandler<PropertyUpdateEventArgs<TaskState>> StateChanged;

        string ErrorMessage { get; }

        /// <remarks>This event can fire on any thread.</remarks>
        event EventHandler<PropertyUpdateEventArgs<string>> ErrorMessageChanged;

        Exception Error { get; }

        /// <remarks>This event can fire on any thread.</remarks>
        event EventHandler<PropertyUpdateEventArgs<Exception>> ErrorChanged;

        void UpdateState(TaskState newState);

        void UpdateState(TaskState newState, Exception error);
    }
}
