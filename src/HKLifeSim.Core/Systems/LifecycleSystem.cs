using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Systems;

public sealed class LifecycleSystem
{
    private const int AgingStartsAt = 50;
    private const int OldAgeRiskStartsAt = 85;
    private const int ForcedDeathAge = 100;

    private readonly Random _random;

    public LifecycleSystem(int seed)
    {
        _random = new Random(seed);
    }

    public void AdvanceYear(GameState state, EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(era);

        state.Age++;
        state.CurrentYear++;

        if (state.Age >= AgingStartsAt)
        {
            state.Stats = state.Stats.ApplyDelta(new StatDelta(Stress: -1, Health: -1));
        }

        if (state.Stats.IsFatal(out var fatalCause))
        {
            state.IsAlive = false;
            state.DeathCause = fatalCause;
            return;
        }

        if (state.Age >= ForcedDeathAge)
        {
            state.IsAlive = false;
            state.DeathCause = "old_age";
            return;
        }

        if (state.Age >= OldAgeRiskStartsAt)
        {
            var deathChancePercent = (state.Age - (OldAgeRiskStartsAt - 1)) * 5;
            if (_random.Next(100) < deathChancePercent)
            {
                state.IsAlive = false;
                state.DeathCause = "old_age";
            }
        }
    }
}
