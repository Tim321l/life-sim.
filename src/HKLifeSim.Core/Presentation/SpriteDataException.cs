namespace HKLifeSim.Core.Presentation;

// Deliberately separate from Data.EventDataException: manifest/sprite asset errors are a
// presentation-layer concern (never affect simulation correctness), distinct from the
// game-content loading errors EventDataException represents.
public sealed class SpriteDataException : Exception
{
    public SpriteDataException()
    {
    }

    public SpriteDataException(string message)
        : base(message)
    {
    }

    public SpriteDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
