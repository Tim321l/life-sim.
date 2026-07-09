using System.Globalization;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Events;

public sealed record EventCondition
{
    public static readonly IReadOnlyCollection<string> ValidOperators =
        [">=", "<=", ">", "<", "==", "hasFlag", "notHasFlag"];

    public required string Op { get; init; }

    public string? StatName { get; init; }

    public required string Value { get; init; }

    public bool Evaluate(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return Op switch
        {
            ">=" => ReadStat(state) >= ParseThreshold(),
            "<=" => ReadStat(state) <= ParseThreshold(),
            ">" => ReadStat(state) > ParseThreshold(),
            "<" => ReadStat(state) < ParseThreshold(),
            "==" => ReadStat(state) == ParseThreshold(),
            "hasFlag" => state.HasFlag(Value),
            "notHasFlag" => !state.HasFlag(Value),
            _ => throw new InvalidOperationException($"Unknown condition operator '{Op}'."),
        };
    }

    private int ReadStat(GameState state) => state.Stats.GetStat(StatName!);

    private int ParseThreshold() => int.Parse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}
