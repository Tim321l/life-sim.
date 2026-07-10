using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Activities;

public sealed class ActivityManager(IReadOnlyList<Activity> pool)
{
    public IReadOnlyList<Activity> GetAvailableActivities(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return pool
            .Where(a => state.Age >= a.MinAge && state.Age <= a.MaxAge)
            .Where(a => a.RequiredFlags.All(state.HasFlag))
            .ToList();
    }

    public ActivityResult ExecuteActivity(GameState state, string activityId, EraConfig era)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(era);

        var activity = pool.FirstOrDefault(a => a.Id == activityId);
        if (activity is null)
        {
            return ActivityResult.ActivityNotFound;
        }

        if (state.Stats.CurrentStamina < activity.StaminaCost)
        {
            return ActivityResult.InsufficientStamina;
        }

        var scaledMoneyCost = -InflationScaler.Scale(new StatDelta(Money: -activity.MoneyCost), era).Money;
        if (state.Stats.Money < scaledMoneyCost)
        {
            return ActivityResult.InsufficientMoney;
        }

        state.Stats = state.Stats.SpendStamina(activity.StaminaCost);
        state.Stats = state.Stats.ApplyDelta(activity.StatModifiers with
        {
            Money = activity.StatModifiers.Money - scaledMoneyCost,
        });

        if (state.Stats.IsFatal(out var cause))
        {
            state.IsAlive = false;
            state.DeathCause = cause;
        }

        return ActivityResult.Success;
    }
}
