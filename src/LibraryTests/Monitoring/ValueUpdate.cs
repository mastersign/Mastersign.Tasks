namespace Mastersign.Tasks.Test.Monitors
{
    public abstract class ValueUpdate
    {
        public abstract object GetNewValue();
    }

    public class ValueUpdate<T> : ValueUpdate
    {
        public T NewValue { get; }

        public ValueUpdate(T newValue)
        {
            NewValue = newValue;
        }

        public override object GetNewValue()
            => NewValue;

        public override bool Equals(object obj)
            => obj != null && obj.GetType() == this.GetType()
                ? Equals(NewValue, ((ValueUpdate<T>)obj).NewValue)
                : false;

        public override int GetHashCode()
            => NewValue == null ? 0 : NewValue.GetHashCode();
    }
}
