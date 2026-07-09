using FluentAssertions;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class InflationScalerTests
{
    private static readonly EraConfig Era1960s = MakeEra(0.02m);
    private static readonly EraConfig Era2024Plus = MakeEra(1.0m);

    [Fact]
    public void Scale_Rounds_Money_By_The_Era_Multiplier()
    {
        var delta = new StatDelta(Money: -100, Stress: 5);

        var scaled = InflationScaler.Scale(delta, Era1960s);

        scaled.Money.Should().Be(-2);
        scaled.Stress.Should().Be(5);
    }

    [Fact]
    public void Scale_Applies_A_Sign_Preserving_Floor_Of_Magnitude_One_When_Rounding_Would_Reach_Zero()
    {
        var delta = new StatDelta(Money: -10);

        var scaled = InflationScaler.Scale(delta, Era1960s);

        scaled.Money.Should().Be(-1);
    }

    [Fact]
    public void Scale_Applies_A_Positive_Sign_Preserving_Floor_Too()
    {
        var delta = new StatDelta(Money: 10);

        var scaled = InflationScaler.Scale(delta, Era1960s);

        scaled.Money.Should().Be(1);
    }

    [Fact]
    public void Scale_Leaves_A_Zero_Money_Delta_Untouched()
    {
        var delta = new StatDelta(Money: 0, Health: -5);

        var scaled = InflationScaler.Scale(delta, Era1960s);

        scaled.Money.Should().Be(0);
        scaled.Health.Should().Be(-5);
    }

    [Fact]
    public void Scale_Is_The_Identity_At_Multiplier_One()
    {
        var delta = new StatDelta(Money: -5000, Health: 3, Stress: -2, FamilyBond: 1, Education: 4, Reputation: -1);

        var scaled = InflationScaler.Scale(delta, Era2024Plus);

        scaled.Should().Be(delta);
    }

    [Fact]
    public void Scale_Throws_For_A_Null_Era()
    {
        var act = () => InflationScaler.Scale(new StatDelta(Money: -100), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static EraConfig MakeEra(decimal multiplier) => new(
        EraId: "test",
        StartYear: 2000,
        EndYear: 2020,
        InflationMultiplier: multiplier,
        AverageHousePrice: 1,
        StartingMoney: 1,
        AvailableCareerTracks: ["tech"],
        EventPoolFiles: ["events_test.json"]);
}
