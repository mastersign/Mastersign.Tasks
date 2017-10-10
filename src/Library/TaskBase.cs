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

        public string QueueTag { get; }

        private float _progress = 0f;
        private bool progressSend = false;
        public float Progress
        {
            get => _progress;
            set
            {
                var newProgress = Math.Max(0f, Math.Min(1f, value));
                if (Math.Abs(_progress - newProgress) < float.Epsilon && progressSend) return;
                var oldProgress = _progress;
                _progress = newProgress;
                OnProgressChanged(oldProgress, newProgress);
                progressSend = true;
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<float>> ProgressChanged;

        protected virtual void OnProgressChanged(float oldValue, float newValue)
        {
            ProgressChanged?.Invoke(this, new PropertyUpdateEventArgs<float>(nameof(Progress), oldValue, newValue));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }

        private string _progressMessage;
        public string ProgressMessage
        {
            get => _progressMessage;
            set
            {
                if (string.Equals(_progressMessage, value)) return;
                var oldProgressMessage = _progressMessage;
                _progressMessage = value;
                OnProgressMessageChanged(oldProgressMessage, value);
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<string>> ProgressMessageChanged;

        protected virtual void OnProgressMessageChanged(string oldValue, string newValue)
        {
            ProgressMessageChanged?.Invoke(this, new PropertyUpdateEventArgs<string>(nameof(ProgressMessage), oldValue, newValue));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressMessage)));
        }

        private TaskState _state = TaskState.Waiting;
        private readonly object stateLock = new object();
        public TaskState State
        {
            get => _state;
            set
            {
                var oldState = _state;
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
                OnStateChanged(oldState, value);
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<TaskState>> StateChanged;

        protected virtual void OnStateChanged(TaskState oldValue, TaskState newValue)
        {
            StateChanged?.Invoke(this, new PropertyUpdateEventArgs<TaskState>(nameof(State), oldValue, newValue));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        }

        private string _errorMessage;
        public string ErrorMessage {
            get => _errorMessage;
            set
            {
                if (string.Equals(_errorMessage, value)) return;
                var oldErrorMessage = _errorMessage;
                _errorMessage = value;
                OnErrorMessageChanged(oldErrorMessage, value);
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<string>> ErrorMessageChanged;

        protected virtual void OnErrorMessageChanged(string oldValue, string newValue)
        {
            ErrorMessageChanged?.Invoke(this, new PropertyUpdateEventArgs<string>(nameof(ErrorMessage), oldValue, newValue));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage)));
        }

        private Exception _error;
        public Exception Error
        {
            get => _error;
            set
            {
                if (_error == value) return;
                var oldError = _error;
                _error = value;
                OnErrorChanged(oldError, value);
            }
        }

        public event EventHandler<PropertyUpdateEventArgs<Exception>> ErrorChanged;

        protected virtual void OnErrorChanged(Exception oldValue, Exception newValue)
        {
            ErrorChanged?.Invoke(this, new PropertyUpdateEventArgs<Exception>(nameof(Error), oldValue, newValue));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
        }

        private readonly List<ITask> _dependencies = new List<ITask>();
        public List<ITask> DependencyList => _dependencies;

        public ITask[] Dependencies => _dependencies.ToArray();

        public bool HasDependencies => _dependencies.Count > 0;

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
