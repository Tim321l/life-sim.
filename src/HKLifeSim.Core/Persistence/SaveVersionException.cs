namespace HKLifeSim.Core.Persistence;

public sealed class SaveVersionException : Exception
{
    public SaveVersionException()
    {
    }

    public SaveVersionException(string message)
        : base(message)
    {
    }

    public SaveVersionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SaveVersionException(int foundVersion, int maxSupportedVersion)
        : base($"Save schemaVersion {foundVersion} is newer than the highest version this build supports ({maxSupportedVersion}). Update the app to load this save.")
    {
        FoundVersion = foundVersion;
        MaxSupportedVersion = maxSupportedVersion;
    }

    public int FoundVersion { get; }

    public int MaxSupportedVersion { get; }
}
