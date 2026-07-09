using FluentAssertions;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class LegacySystemTests
{
    [Fact]
    public void GenerateLegacy_Never_Inherits_Debt()
    {
        var state = CreateState(money: -50_000);

        var legacy = LegacySystem.GenerateLegacy(state);

        legacy.InheritedMoney.Should().Be(0);
    }

    [Fact]
    public void GenerateLegacy_Inherits_Half_Of_Positive_Money_By_Default_Rounded_Down()
    {
        var state = CreateState(money: 999);

        var legacy = LegacySystem.GenerateLegacy(state);

        legacy.InheritedMoney.Should().Be(499);
    }

    [Fact]
    public void GenerateLegacy_Honors_A_Custom_Inheritance_Rate()
    {
        var state = CreateState(money: 1000);

        var legacy = LegacySystem.GenerateLegacy(state, inheritanceRate: 0.25m);

        legacy.InheritedMoney.Should().Be(250);
    }

    [Fact]
    public void GenerateLegacy_Only_Carries_Flags_With_The_Legacy_Prefix()
    {
        var state = CreateState(money: 0);
        state.SetFlag("legacy_owns_flat");
        state.SetFlag("legacy_emigrated");
        state.SetFlag("married");
        state.SetFlag("dating");

        var legacy = LegacySystem.GenerateLegacy(state);

        legacy.InheritedFlags.Should().BeEquivalentTo(["legacy_owns_flat", "legacy_emigrated"]);
    }

    [Fact]
    public void GenerateLegacy_Carries_Half_Of_Reputation()
    {
        var state = CreateState(money: 0, reputation: 41);

        var legacy = LegacySystem.GenerateLegacy(state);

        legacy.FamilyReputationCarryOver.Should().Be(20);
    }

    [Fact]
    public void GenerateLegacy_Records_The_Source_Player_And_Era()
    {
        var state = CreateState(money: 0);

        var legacy = LegacySystem.GenerateLegacy(state);

        legacy.SourcePlayerId.Should().Be(state.PlayerId);
        legacy.SourceEraId.Should().Be(state.EraId);
    }

    [Fact]
    public void GenerateLegacy_Throws_For_A_Null_State()
    {
        var act = () => LegacySystem.GenerateLegacy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static GameState CreateState(int money, int reputation = 10) => new()
    {
        PlayerId = "player-1",
        EraId = "2024plus",
        Stats = new StatBlock(Money: money, Health: 50, Stress: 50, FamilyBond: 50, Education: 50, Reputation: reputation),
    };
}
