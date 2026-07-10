using FluentAssertions;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class LifecycleSystemTests
{
    private static readonly EraConfig Era = new(
        EraId: "2024plus",
        StartYear: 2024,
        EndYear: 2045,
        InflationMultiplier: 1.0m,
        AverageHousePrice: 8_000_000m,
        StartingMoney: 20_000,
        AvailableCareerTracks: ["tech"],
        EventPoolFiles: ["events_2024plus.json"]);

    [Fact]
    public void AdvanceYear_Resets_CurrentStamina_To_MaxStamina_After_Aging_Up()
    {
        var state = new GameState
        {
            PlayerId = "test",
            EraId = "2024plus",
            Age = 25,
            CurrentYear = 2049,
            Stats = new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 3 },
        };
        var lifecycle = new LifecycleSystem(seed: 1);

        lifecycle.AdvanceYear(state, Era);

        state.Age.Should().Be(26);
        state.Stats.CurrentStamina.Should().Be(state.Stats.MaxStamina);
    }
}
