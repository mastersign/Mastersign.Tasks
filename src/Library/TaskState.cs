using System;
using System.Collections.Generic;
using System.Text;

namespace Mastersign.Tasks
{
    public enum TaskState
    {
        /// <summary>
        /// Task was not started yet
        /// </summary>
        Waiting,

        /// <summary>
        /// A dependency of this task failed or was cancelled
        /// </summary>
        Obsolete,

        /// <summary>
        /// Task was started and is getting processed by worker
        /// </summary>
        InProgress,

        /// <summary>
        /// Task was cancelled or has failed but worker is busy cleaning up
        /// </summary>
        CleaningUp,

        /// <summary>
        /// Task was cancelled and worker has stopped processing it
        /// </summary>
        Canceled,

        /// <summary>
        /// Task has failed and worker has stopped processing it
        /// </summary>
        Failed,

        /// <summary>
        /// Task has fininshed successful and worker has stopped processing it
        /// </summary>
        Succeeded
    }
}
