using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Mastersign.Tasks
{
    public abstract class TaskBase : ITask
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected TaskBase(string queueTag)
        {
            QueueTag = queueTag;
        }

        public string QueueTag { get; private set; }

        private float _progress = 0f;
        public float Progress
        {
            get => _progress;
            set
            {
                var newProgress = Math.Max(0f, Math.Min(1f, value));
                if (Math.Abs(_progress - newProgress) < float.Epsilon) return;
                _progress = newProgress;
                OnProgressChanged();
            }
        }

        public event EventHandler ProgressChanged;

        protected void OnProgressChanged()
        {
            ProgressChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }

        private string _progressMessage;
        public string ProgressMessage
        {
            get => _progressMessage;
            set
            {
                if (string.Equals(_progressMessage, value)) return;
                _progressMessage = value;
                OnProgressMessageChanged();
            }
        }

        public event EventHandler ProgressMessageChanged;

        private void OnProgressMessageChanged()
        {
            ProgressMessageChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressMessage)));
        }

        private TaskState _state = TaskState.Waiting;
        private readonly object stateLock = new object();
        public TaskState State
        {
            get => _state;
            set
            {
                lock (stateLock)
                {
                    if (_state == value) return;
                    var invalid = false;
                    switch (_state)
                    {
                        case TaskState.Waiting:
                            if (value != TaskState.InProgress &&
                                value != TaskState.Obsolete)
                            {
                                invalid = true;
                            }
                            break;
                        case TaskState.InProgress:
                            if (value != TaskState.CleaningUp &&
                                value != TaskState.Succeeded &&
                                value != TaskState.Canceled &&
                                value != TaskState.Failed)
                            {
                                invalid = true;
                            }
                            break;
                        case TaskState.CleaningUp:
                            if (value != TaskState.Succeeded &&
                                value != TaskState.Canceled &&
                                value != TaskState.Failed)
                            {
                                invalid = true;
                            }
                            break;
                        case TaskState.Canceled:
                        case TaskState.Failed:
                        case TaskState.Succeeded:
                        case TaskState.Obsolete:
                            invalid = true;
                            break;
                        default:
                            throw new NotSupportedException();
                    }
                    if (invalid)
                    {
                        throw new InvalidOperationException($"Invalid task state transition from {_state} to {value}.");
                    }
                    _state = value;
                }
                OnStateChanged();
            }
        }

        public event EventHandler StateChanged;

        private void OnStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        }

        private string _errorMessage;
        public string ErrorMessage {
            get => _errorMessage;
            set
            {
                if (string.Equals(_errorMessage, value)) return;
                _errorMessage = value;
                OnErrorMessageChanged();
            }
        }

        public event EventHandler ErrorMessageChanged;

        private void OnErrorMessageChanged()
        {
            ErrorMessageChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
        }

        private Exception _error;
        public Exception Error
        {
            get => _error;
            set
            {
                if (_error == value) return;
                _error = value;
                OnErrorChanged();
            }
        }

        public event EventHandler ErrorChanged;

        private void OnErrorChanged()
        {
            ErrorChanged?.Invoke(this, EventArgs.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
        }

        private readonly List<ITask> _dependencies = new List<ITask>();
        public List<ITask> DependencyList => _dependencies;

        public ITask[] Dependencies => _dependencies.ToArray();

        public void UpdateProgress(string message, float progress)
        {
            Progress = progress;
            ProgressMessage = message;
        }

        public void UpdateState(TaskState newState)
        {
            Error = null;
            ErrorMessage = null;
            State = newState;
        }

        public void UpdateState(TaskState newState, string errorMessage)
        {
            Error = null;
            ErrorMessage = errorMessage;
            State = newState;
        }

        public void UpdateState(TaskState newState, Exception error)
        {
            Error = error;
            ErrorMessage = error?.Message;
            State = newState;
        }

        public void UpdateState(TaskState newState, string errorMessage, Exception error)
        {
            Error = error;
            ErrorMessage = errorMessage;
            State = newState;
        }
    }
}
