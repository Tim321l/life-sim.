using FluentAssertions;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Tests;

public sealed class StatDeltaTests
{
    [Fact]
    public void Scale_Multiplies_Only_Money_And_Leaves_Other_Stats_Untouched()
    {
        var delta = new StatDelta(Money: 100, Health: 5, Stress: -3, FamilyBond: 2, Education: 1, Reputation: 4);

        var scaled = delta.Scale(2.5m);

        scaled.Money.Should().Be(250);
        scaled.Health.Should().Be(5);
        scaled.Stress.Should().Be(-3);
        scaled.FamilyBond.Should().Be(2);
        scaled.Education.Should().Be(1);
        scaled.Reputation.Should().Be(4);
    }

    [Theory]
    [InlineData(101, 0.5, 51)]
    [InlineData(-101, 0.5, -51)]
    [InlineData(100, 0.005, 1)]
    [InlineData(-100, 0.005, -1)]
    public void Scale_Rounds_Money_Away_From_Zero(int money, double multiplier, int expected)
    {
        var delta = new StatDelta(Money: money);

        var scaled = delta.Scale((decimal)multiplier);

        scaled.Money.Should().Be(expected);
    }
}
