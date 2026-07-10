using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Presentation;

public static class AppearanceCalculator
{
    public static AppearanceState Calculate(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsAlive)
        {
            return new AppearanceState(Stage.Tombstone, Mood.Dead);
        }

        return new AppearanceState(StageFor(state.Age), MoodFor(state.Stats));
    }

    private static Stage StageFor(int age) => age switch
    {
        <= 2 => Stage.Baby,
        <= 11 => Stage.Child,
        <= 17 => Stage.Teen,
        <= 59 => Stage.Adult,
        _ => Stage.Elder,
    };

    private static Mood MoodFor(StatBlock stats)
    {
        if (stats.Health < 30)
        {
            return Mood.Sick;
        }

        if (stats.Stress > 70)
        {
            return Mood.Stressed;
        }

        if (stats.CurrentStamina < stats.MaxStamina * 0.2)
        {
            return Mood.Tired;
        }

        return Mood.Idle;
    }
}
