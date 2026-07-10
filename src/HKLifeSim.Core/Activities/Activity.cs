using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Activities;

public sealed record Activity
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required int StaminaCost { get; init; }

    public int MoneyCost { get; init; }

    public StatDelta StatModifiers { get; init; }

    public required int MinAge { get; init; }

    public required int MaxAge { get; init; }

    public IReadOnlyList<string> RequiredFlags { get; init; } = [];
}
