using System.Globalization;
using FluentAssertions;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;

namespace HKLifeSim.Core.Tests;

public sealed class EventConditionTests
{
    [Theory]
    [InlineData(">=", 50, 50, true)]
    [InlineData(">=", 49, 50, false)]
    [InlineData("<=", 50, 50, true)]
    [InlineData("<=", 51, 50, false)]
    [InlineData(">", 51, 50, true)]
    [InlineData(">", 50, 50, false)]
    [InlineData("<", 49, 50, true)]
    [InlineData("<", 50, 50, false)]
    [InlineData("==", 50, 50, true)]
    [InlineData("==", 49, 50, false)]
    public void Evaluate_Compares_Stat_Against_Threshold(string op, int statValue, int threshold, bool expected)
    {
        var condition = new EventCondition
        {
            Op = op,
            StatName = "Stress",
            Value = threshold.ToString(CultureInfo.InvariantCulture),
        };
        var state = CreateState(new StatBlock(Money: 0, Health: 50, Stress: statValue, FamilyBond: 50, Education: 50, Reputation: 50));

        condition.Evaluate(state).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_HasFlag_Is_True_When_Flag_Is_Present()
    {
        var condition = new EventCondition { Op = "hasFlag", Value = "married" };
        var state = CreateState(DefaultStats, "married");

        condition.Evaluate(state).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_HasFlag_Is_False_When_Flag_Is_Absent()
    {
        var condition = new EventCondition { Op = "hasFlag", Value = "married" };
        var state = CreateState(DefaultStats);

        condition.Evaluate(state).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NotHasFlag_Is_True_When_Flag_Is_Absent()
    {
        var condition = new EventCondition { Op = "notHasFlag", Value = "dropped_out" };
        var state = CreateState(DefaultStats);

        condition.Evaluate(state).Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NotHasFlag_Is_False_When_Flag_Is_Present()
    {
        var condition = new EventCondition { Op = "notHasFlag", Value = "dropped_out" };
        var state = CreateState(DefaultStats, "dropped_out");

        condition.Evaluate(state).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Throws_For_An_Unrecognized_Operator()
    {
        var condition = new EventCondition { Op = "~=", StatName = "Stress", Value = "10" };
        var state = CreateState(DefaultStats);

        var act = () => condition.Evaluate(state);

        act.Should().Throw<InvalidOperationException>();
    }

    private static StatBlock DefaultStats { get; } = new(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10);

    private static GameState CreateState(StatBlock stats, params string[] flags)
    {
        var state = new GameState
        {
            PlayerId = "player-1",
            EraId = "2024plus",
            Age = 18,
            Stats = stats,
        };

        foreach (var flag in flags)
        {
            state.SetFlag(flag);
        }

        return state;
    }
}
