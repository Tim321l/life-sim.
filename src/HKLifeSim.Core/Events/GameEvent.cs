namespace HKLifeSim.Core.Events;

public sealed record GameEvent
{
    public required string Id { get; init; }

    public string EraId { get; init; } = string.Empty;

    public required int MinAge { get; init; }

    public required int MaxAge { get; init; }

    public required int Weight { get; init; }

    public bool IsUnique { get; init; }

    public required string Category { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public IReadOnlyList<EventCondition> Conditions { get; init; } = [];

    public required IReadOnlyList<EventChoice> Choices { get; init; }

    public string? FollowUpEventId { get; init; }
}
