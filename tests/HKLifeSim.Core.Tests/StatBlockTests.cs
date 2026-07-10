using FluentAssertions;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Tests;

public sealed class StatBlockTests
{
    private static readonly EraConfig TestEra = new(
        EraId: "2024plus",
        StartYear: 2024,
        EndYear: 2045,
        InflationMultiplier: 1.0m,
        AverageHousePrice: 8_000_000m,
        StartingMoney: 20_000,
        AvailableCareerTracks: ["tech"],
        EventPoolFiles: ["events_2024plus.json"]);

    [Fact]
    public void ApplyDelta_Clamps_Health_At_Upper_Bound()
    {
        var stats = new StatBlock(Money: 0, Health: 95, Stress: 0, FamilyBond: 0, Education: 0, Reputation: 0);

        var result = stats.ApplyDelta(new StatDelta(Health: 20));

        result.Health.Should().Be(100);
    }

    [Fact]
    public void ApplyDelta_Clamps_Health_At_Lower_Bound()
    {
        var stats = new StatBlock(Money: 0, Health: 5, Stress: 0, FamilyBond: 0, Education: 0, Reputation: 0);

        var result = stats.ApplyDelta(new StatDelta(Health: -20));

        result.Health.Should().Be(0);
    }

    [Theory]
    [InlineData(nameof(StatBlock.Stress))]
    [InlineData(nameof(StatBlock.FamilyBond))]
    [InlineData(nameof(StatBlock.Education))]
    [InlineData(nameof(StatBlock.Reputation))]
    public void ApplyDelta_Clamps_All_Bounded_Stats_Between_Zero_And_A_Hundred(string statName)
    {
        var baseline = new StatBlock(Money: 0, Health: 50, Stress: 50, FamilyBond: 50, Education: 50, Reputation: 50);
        var hugePositive = DeltaFor(statName, 1000);
        var hugeNegative = DeltaFor(statName, -1000);

        var upper = baseline.ApplyDelta(hugePositive);
        var lower = baseline.ApplyDelta(hugeNegative);

        Read(upper, statName).Should().Be(100);
        Read(lower, statName).Should().Be(0);
    }

    [Fact]
    public void ApplyDelta_Allows_Money_To_Go_Negative_Without_Clamping()
    {
        var stats = new StatBlock(Money: 100, Health: 50, Stress: 50, FamilyBond: 50, Education: 50, Reputation: 50);

        var result = stats.ApplyDelta(new StatDelta(Money: -500));

        result.Money.Should().Be(-400);
    }

    [Fact]
    public void IsFatal_Is_True_When_Health_Reaches_Zero_With_Health_Zero_Cause()
    {
        var stats = new StatBlock(Money: 0, Health: 0, Stress: 0, FamilyBond: 0, Education: 0, Reputation: 0);

        var isFatal = stats.IsFatal(out var cause);

        isFatal.Should().BeTrue();
        cause.Should().Be("health_zero");
    }

    [Fact]
    public void IsFatal_Is_False_When_Health_Is_One()
    {
        var stats = new StatBlock(Money: 0, Health: 1, Stress: 0, FamilyBond: 0, Education: 0, Reputation: 0);

        stats.IsFatal(out _).Should().BeFalse();
    }

    [Fact]
    public void IsFatal_Is_True_When_Stress_Reaches_A_Hundred_With_Stress_Breakdown_Cause()
    {
        var stats = new StatBlock(Money: 0, Health: 50, Stress: 100, FamilyBond: 0, Education: 0, Reputation: 0);

        var isFatal = stats.IsFatal(out var cause);

        isFatal.Should().BeTrue();
        cause.Should().Be("stress_breakdown");
    }

    [Fact]
    public void IsFatal_Is_False_When_Stress_Is_NinetyNine()
    {
        var stats = new StatBlock(Money: 0, Health: 50, Stress: 99, FamilyBond: 0, Education: 0, Reputation: 0);

        stats.IsFatal(out _).Should().BeFalse();
    }

    [Fact]
    public void CreateStarting_Uses_Era_Starting_Money_When_No_Legacy_Given()
    {
        var stats = StatBlock.CreateStarting(TestEra, legacy: null);

        stats.Money.Should().Be(20_000);
        stats.Health.Should().Be(80);
        stats.Stress.Should().Be(10);
        stats.FamilyBond.Should().Be(50);
        stats.Education.Should().Be(10);
        stats.Reputation.Should().Be(10);
    }

    [Fact]
    public void CreateStarting_Adds_Inherited_Money_From_Legacy()
    {
        var legacy = new LegacyRecord(
            SourcePlayerId: "parent-1",
            SourceEraId: "2024plus",
            InheritedMoney: 5_000,
            InheritedFlags: [],
            FamilyReputationCarryOver: 0);

        var stats = StatBlock.CreateStarting(TestEra, legacy);

        stats.Money.Should().Be(25_000);
    }

    [Fact]
    public void CreateStarting_Adds_Reputation_Carry_Over_From_Legacy()
    {
        var legacy = new LegacyRecord(
            SourcePlayerId: "parent-1",
            SourceEraId: "2024plus",
            InheritedMoney: 0,
            InheritedFlags: [],
            FamilyReputationCarryOver: 15);

        var stats = StatBlock.CreateStarting(TestEra, legacy);

        stats.Reputation.Should().Be(25);
    }

    [Fact]
    public void CreateStarting_Clamps_Reputation_Carry_Over_At_The_Upper_Bound()
    {
        var legacy = new LegacyRecord(
            SourcePlayerId: "parent-1",
            SourceEraId: "2024plus",
            InheritedMoney: 0,
            InheritedFlags: [],
            FamilyReputationCarryOver: 1000);

        var stats = StatBlock.CreateStarting(TestEra, legacy);

        stats.Reputation.Should().Be(100);
    }

    [Fact]
    public void Default_Stamina_Parameters_Are_Fifty_When_Not_Specified()
    {
        var stats = new StatBlock(Money: 0, Health: 50, Stress: 50, FamilyBond: 50, Education: 50, Reputation: 50);

        stats.MaxStamina.Should().Be(50);
        stats.CurrentStamina.Should().Be(50);
    }

    [Fact]
    public void ResetStamina_Sets_CurrentStamina_To_MaxStamina()
    {
        var stats = new StatBlock(Money: 0, Health: 50, Stress: 50, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 5 };

        var result = stats.ResetStamina();

        result.CurrentStamina.Should().Be(50);
    }

    [Fact]
    public void ApplyDelta_Preserves_Stamina_Fields_Untouched_By_The_Delta()
    {
        var stats = new StatBlock(Money: 0, Health: 50, Stress: 50, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 12 };

        var result = stats.ApplyDelta(new StatDelta(Health: 5));

        result.MaxStamina.Should().Be(50);
        result.CurrentStamina.Should().Be(12);
    }

    private static StatDelta DeltaFor(string statName, int value) => statName switch
    {
        nameof(StatBlock.Stress) => new StatDelta(Stress: value),
        nameof(StatBlock.FamilyBond) => new StatDelta(FamilyBond: value),
        nameof(StatBlock.Education) => new StatDelta(Education: value),
        nameof(StatBlock.Reputation) => new StatDelta(Reputation: value),
        _ => throw new ArgumentOutOfRangeException(nameof(statName)),
    };

    private static int Read(StatBlock stats, string statName) => statName switch
    {
        nameof(StatBlock.Stress) => stats.Stress,
        nameof(StatBlock.FamilyBond) => stats.FamilyBond,
        nameof(StatBlock.Education) => stats.Education,
        nameof(StatBlock.Reputation) => stats.Reputation,
        _ => throw new ArgumentOutOfRangeException(nameof(statName)),
    };
}
