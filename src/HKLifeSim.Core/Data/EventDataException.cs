namespace HKLifeSim.Core.Data;

public sealed class EventDataException : Exception
{
    public EventDataException()
    {
    }

    public EventDataException(string message)
        : base(message)
    {
    }

    public EventDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
