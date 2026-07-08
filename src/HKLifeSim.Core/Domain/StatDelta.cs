namespace HKLifeSim.Core.Domain;

public readonly record struct StatDelta(
    int Money = 0,
    int Health = 0,
    int Stress = 0,
    int FamilyBond = 0,
    int Education = 0,
    int Reputation = 0)
{
    public StatDelta Scale(decimal multiplier) =>
        this with { Money = (int)Math.Round(Money * multiplier, MidpointRounding.AwayFromZero) };
}
