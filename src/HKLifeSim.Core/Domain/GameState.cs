using System.Diagnostics.CodeAnalysis;

namespace HKLifeSim.Core.Domain;

public sealed class GameState
{
    public required string PlayerId { get; set; }

    public required string EraId { get; set; }

    public int Age { get; set; }

    public int CurrentYear { get; set; }

    public StatBlock Stats { get; set; }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "GameState is a mutable POCO deserialized by System.Text.Json; required members on this type are incompatible with JsonObjectCreationHandling.Populate, so collections need a setter.")]
    public HashSet<string> FlagsSet { get; set; } = [];

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "GameState is a mutable POCO deserialized by System.Text.Json; required members on this type are incompatible with JsonObjectCreationHandling.Populate, so collections need a setter.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "EventHistory is an ordered, append-only log of event ids; List<string> is the domain type per spec and round-trips through JSON as-is.")]
    public List<string> EventHistory { get; set; } = [];

    public LegacyRecord? InheritedLegacy { get; set; }

    public int RngSeed { get; set; }

    public bool IsAlive { get; set; } = true;

    public string? DeathCause { get; set; }

    public CharacterProfile? Profile { get; set; }

    public bool HasFlag(string flag) => FlagsSet.Contains(flag);

    public void SetFlag(string flag) => FlagsSet.Add(flag);
}
