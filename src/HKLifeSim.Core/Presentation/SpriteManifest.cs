namespace HKLifeSim.Core.Presentation;

public sealed record SpriteManifest
{
    public required int SchemaVersion { get; init; }

    public required IReadOnlyDictionary<string, StageSheet> Stages { get; init; }

    public required IReadOnlyDictionary<string, IconDef> Icons { get; init; }

    public required IReadOnlyDictionary<string, ActionDef> Actions { get; init; }
}

public sealed record StageSheet
{
    public required string Sheet { get; init; }

    // Keyed by pose/mood-face name: "stand"/"sit" are the two action-eligible base poses every
    // stage must define; "sick"/"stressed"/"tired" are optional mood face-variant rows shown when
    // idling (no action playing) under that mood. All resolved uniformly by name — the caller
    // decides which key to ask for.
    public required IReadOnlyDictionary<string, PoseDef> Poses { get; init; }
}

public sealed record PoseDef
{
    public required int Row { get; init; }

    public required int Frames { get; init; }

    public required int Ms { get; init; }
}

public sealed record IconDef
{
    public required string File { get; init; }

    public required int Frames { get; init; }

    public required int Ms { get; init; }

    // "front" (positioned in front of/below the character) or "overlay" (covers the character).
    public required string Anchor { get; init; }
}

public sealed record ActionDef
{
    // Must be "stand" or "sit" — actions only ever use the two base poses, never a mood face-variant.
    public required string Pose { get; init; }

    public string? Icon { get; init; }

    public required int DurationMs { get; init; }

    // Overrides the pose's own Ms for the duration of this action (e.g. faster bob for skipping rope).
    public int? PoseMs { get; init; }
}
