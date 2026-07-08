namespace HKLifeSim.Core.Domain;

public readonly record struct StatBlock(
    int Money,
    int Health,
    int Stress,
    int FamilyBond,
    int Education,
    int Reputation)
{
    public static StatBlock CreateStarting(EraConfig era, LegacyRecord? legacy)
    {
        ArgumentNullException.ThrowIfNull(era);

        return new(
            Money: era.StartingMoney + (legacy?.InheritedMoney ?? 0),
            Health: 80,
            Stress: 10,
            FamilyBond: 50,
            Education: 10,
            Reputation: 10);
    }

    public StatBlock ApplyDelta(StatDelta delta) =>
        new(
            Money: Money + delta.Money,
            Health: Clamp(Health + delta.Health),
            Stress: Clamp(Stress + delta.Stress),
            FamilyBond: Clamp(FamilyBond + delta.FamilyBond),
            Education: Clamp(Education + delta.Education),
            Reputation: Clamp(Reputation + delta.Reputation));

    public bool IsFatal(out string cause)
    {
        if (Health <= 0)
        {
            cause = "health_zero";
            return true;
        }

        if (Stress >= 100)
        {
            cause = "stress_breakdown";
            return true;
        }

        cause = string.Empty;
        return false;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}
