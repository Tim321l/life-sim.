using System.Diagnostics.CodeAnalysis;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Data;

public sealed class EraFile
{
    public required int SchemaVersion { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "EraFile is a JSON transport DTO deserialized by System.Text.Json; it is not mutated after loading.")]
    public required IReadOnlyList<EraConfig> Eras { get; set; }
}
