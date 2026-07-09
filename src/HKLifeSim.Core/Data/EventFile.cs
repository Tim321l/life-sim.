using System.Diagnostics.CodeAnalysis;
using HKLifeSim.Core.Events;

namespace HKLifeSim.Core.Data;

public sealed class EventFile
{
    public required int SchemaVersion { get; set; }

    public required string EraId { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "EventFile is a JSON transport DTO deserialized by System.Text.Json; it is not mutated after loading.")]
    public required IReadOnlyList<GameEvent> Events { get; set; }
}
