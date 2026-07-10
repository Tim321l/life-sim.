using System.Diagnostics.CodeAnalysis;
using HKLifeSim.Core.Activities;

namespace HKLifeSim.Core.Data;

public sealed class ActivityFile
{
    public required int SchemaVersion { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "ActivityFile is a JSON transport DTO deserialized by System.Text.Json; it is not mutated after loading.")]
    public required IReadOnlyList<Activity> Activities { get; set; }
}
