using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Events;

public sealed class EventEngine
{
    private readonly IReadOnlyList<GameEvent> _pool;
    private readonly EraConfig _era;
    private readonly Random _random;
    private string? _pendingFollowUpId;

    public EventEngine(IReadOnlyList<GameEvent> pool, EraConfig era, int seed)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(era);

        _pool = pool;
        _era = era;
        _random = new Random(seed);
    }

    public GameEvent SelectNextEvent(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_pendingFollowUpId is not null)
        {
            var followUp = _pool.FirstOrDefault(e => e.Id == _pendingFollowUpId);
            if (followUp is not null && state.Age >= followUp.MinAge && state.Age <= followUp.MaxAge)
            {
                _pendingFollowUpId = null;
                return followUp;
            }
        }

        var candidates = _pool
            .Where(e => e.EraId == state.EraId)
            .Where(e => state.Age >= e.MinAge && state.Age <= e.MaxAge)
            .Where(e => !e.IsUnique || !state.EventHistory.Contains(e.Id))
            .Where(e => e.Conditions.All(c => c.Evaluate(state)))
            .ToList();

        if (candidates.Count == 0)
        {
            var fallbackId = $"generic_daily_life_{state.EraId}";
            return _pool.First(e => e.Id == fallbackId);
        }

        return WeightedPick(candidates);
    }

    public void ApplyChoice(GameState state, GameEvent gameEvent, string choiceId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(gameEvent);

        var choice = gameEvent.Choices.FirstOrDefault(c => c.Id == choiceId)
            ?? throw new ArgumentException($"Unknown choice id '{choiceId}' for event '{gameEvent.Id}'.", nameof(choiceId));

        var effects = choice.AbsoluteMoney ? choice.Effects : InflationScaler.Scale(choice.Effects, _era);
        state.Stats = state.Stats.ApplyDelta(effects);
        state.EventHistory.Add(gameEvent.Id);

        foreach (var flag in choice.FlagsToSet)
        {
            state.SetFlag(flag);
        }

        _pendingFollowUpId = choice.FollowUpEventId ?? gameEvent.FollowUpEventId;

        if (state.Stats.IsFatal(out var cause))
        {
            state.IsAlive = false;
            state.DeathCause = cause;
        }
    }

    public string PickRandomChoiceId(GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        var index = _random.Next(gameEvent.Choices.Count);
        return gameEvent.Choices[index].Id;
    }

    private GameEvent WeightedPick(List<GameEvent> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var totalWeight = candidates.Sum(e => e.Weight);
        if (totalWeight <= 0)
        {
            return candidates[_random.Next(candidates.Count)];
        }

        var roll = _random.Next(totalWeight);
        var cumulative = 0;
        foreach (var candidate in candidates)
        {
            cumulative += candidate.Weight;
            if (roll < cumulative)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }
}
