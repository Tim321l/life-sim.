using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Events;

public sealed record EventChoice
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public StatDelta Effects { get; init; }

    public IReadOnlyList<string> FlagsToSet { get; init; } = [];

    public string? FollowUpEventId { get; init; }
}
