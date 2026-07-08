using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Persistence;

public sealed class SaveEnvelope
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; set; }

    public DateTimeOffset SavedAtUtc { get; set; }

    public required GameState State { get; set; }
}
