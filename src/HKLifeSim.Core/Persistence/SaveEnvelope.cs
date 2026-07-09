using System.Diagnostics.CodeAnalysis;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Persistence;

public sealed class SaveEnvelope
{
    public const int CurrentSchemaVersion = 2;

    public required int SchemaVersion { get; set; }

    public DateTimeOffset SavedAtUtc { get; set; }

    public required GameState State { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "SaveEnvelope is a mutable POCO deserialized by System.Text.Json; schemaVersion 1 saves omit this field entirely and must default to an empty lineage.")]
    public IReadOnlyList<LegacyRecord> Lineage { get; set; } = [];
}
