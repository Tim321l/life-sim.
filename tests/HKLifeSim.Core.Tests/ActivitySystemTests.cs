using FluentAssertions;
using HKLifeSim.Core.Activities;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Tests;

public sealed class ActivitySystemTests
{
    private static readonly EraConfig Era2024Plus = MakeEra("2024plus", 1.0m);
    private static readonly EraConfig Era1960s = MakeEra("1960s", 0.02m);

    private static readonly Activity FreeActivity = new()
    {
        Id = "free_activity",
        Name = "免費活動",
        Type = "test",
        StaminaCost = 0,
        MoneyCost = 0,
        MinAge = 0,
        MaxAge = 100,
    };

    private static readonly Activity StaminaHeavyActivity = new()
    {
        Id = "stamina_heavy",
        Name = "體力活動",
        Type = "physical",
        StaminaCost = 30,
        MoneyCost = 0,
        MinAge = 0,
        MaxAge = 100,
    };

    private static readonly Activity CostlyActivity = new()
    {
        Id = "drivers_license_test",
        Name = "考駕照",
        Type = "special",
        StaminaCost = 20,
        MoneyCost = 5000,
        MinAge = 18,
        MaxAge = 100,
    };

    private static readonly Activity StressfulActivity = new()
    {
        Id = "stressful_activity",
        Name = "壓力活動",
        Type = "test",
        StaminaCost = 0,
        MoneyCost = 0,
        StatModifiers = new StatDelta(Stress: 50),
        MinAge = 0,
        MaxAge = 100,
    };

    private static readonly Activity ChildOnlyActivity = new()
    {
        Id = "child_only",
        Name = "兒童活動",
        Type = "test",
        StaminaCost = 0,
        MoneyCost = 0,
        MinAge = 3,
        MaxAge = 11,
    };

    private static readonly Activity FlaggedActivity = new()
    {
        Id = "flagged_activity",
        Name = "有條件嘅活動",
        Type = "test",
        StaminaCost = 0,
        MoneyCost = 0,
        MinAge = 0,
        MaxAge = 100,
        RequiredFlags = ["has_permission"],
    };

    [Fact]
    public void ExecuteActivity_Succeeds_When_CurrentStamina_Is_Zero_And_Cost_Is_Zero()
    {
        var manager = new ActivityManager([FreeActivity]);
        var state = MakeState(new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 0 });

        var result = manager.ExecuteActivity(state, FreeActivity.Id, Era2024Plus);

        result.Should().Be(ActivityResult.Success);
    }

    [Fact]
    public void ExecuteActivity_Fails_With_InsufficientStamina_And_Leaves_State_Completely_Unpolluted()
    {
        var manager = new ActivityManager([StaminaHeavyActivity]);
        var originalStats = new StatBlock(Money: 1000, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 10 };
        var state = MakeState(originalStats);

        var result = manager.ExecuteActivity(state, StaminaHeavyActivity.Id, Era2024Plus);

        result.Should().Be(ActivityResult.InsufficientStamina);
        state.Stats.Should().Be(originalStats);
        state.IsAlive.Should().BeTrue();
        state.DeathCause.Should().BeNull();
    }

    [Fact]
    public void ExecuteActivity_Fails_With_InsufficientMoney_And_Leaves_State_Completely_Unpolluted()
    {
        var manager = new ActivityManager([CostlyActivity]);
        var originalStats = new StatBlock(Money: 100, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 50 };
        var state = MakeState(originalStats, age: 25);

        var result = manager.ExecuteActivity(state, CostlyActivity.Id, Era2024Plus);

        result.Should().Be(ActivityResult.InsufficientMoney);
        state.Stats.Should().Be(originalStats);
        state.IsAlive.Should().BeTrue();
        state.DeathCause.Should().BeNull();
    }

    [Fact]
    public void ExecuteActivity_Scales_MoneyCost_Per_Era_Using_InflationScaler()
    {
        var manager = new ActivityManager([CostlyActivity]);
        var state2024 = MakeState(new StatBlock(Money: 10_000, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 50 }, age: 25);
        var state1960s = MakeState(new StatBlock(Money: 10_000, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 50 }, age: 25);

        manager.ExecuteActivity(state2024, CostlyActivity.Id, Era2024Plus).Should().Be(ActivityResult.Success);
        manager.ExecuteActivity(state1960s, CostlyActivity.Id, Era1960s).Should().Be(ActivityResult.Success);

        state2024.Stats.Money.Should().Be(10_000 - 5_000);
        state1960s.Stats.Money.Should().Be(10_000 - 100);
    }

    [Fact]
    public void ExecuteActivity_Sets_IsAlive_False_And_DeathCause_When_StatModifiers_Push_Stress_To_A_Hundred()
    {
        var manager = new ActivityManager([StressfulActivity]);
        var state = MakeState(new StatBlock(Money: 0, Health: 80, Stress: 60, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 50 });

        var result = manager.ExecuteActivity(state, StressfulActivity.Id, Era2024Plus);

        result.Should().Be(ActivityResult.Success);
        state.Stats.Stress.Should().Be(100);
        state.IsAlive.Should().BeFalse();
        state.DeathCause.Should().Be("stress_breakdown");
    }

    [Fact]
    public void ExecuteActivity_Returns_ActivityNotFound_And_Does_Not_Throw_For_An_Unknown_Id()
    {
        var manager = new ActivityManager([FreeActivity]);
        var originalStats = new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10);
        var state = MakeState(originalStats);

        var act = () => manager.ExecuteActivity(state, "does_not_exist", Era2024Plus);

        var result = act.Should().NotThrow().Which;
        result.Should().Be(ActivityResult.ActivityNotFound);
        state.Stats.Should().Be(originalStats);
    }

    [Fact]
    public void GetAvailableActivities_Filters_By_Age()
    {
        var manager = new ActivityManager([FreeActivity, ChildOnlyActivity]);
        var childState = MakeState(DefaultStats, age: 6);
        var adultState = MakeState(DefaultStats, age: 30);

        manager.GetAvailableActivities(childState).Select(a => a.Id).Should().Contain(ChildOnlyActivity.Id);
        manager.GetAvailableActivities(adultState).Select(a => a.Id).Should().NotContain(ChildOnlyActivity.Id);
    }

    [Fact]
    public void GetAvailableActivities_Filters_By_RequiredFlags()
    {
        var manager = new ActivityManager([FlaggedActivity]);
        var stateWithoutFlag = MakeState(DefaultStats);
        var stateWithFlag = MakeState(DefaultStats);
        stateWithFlag.SetFlag("has_permission");

        manager.GetAvailableActivities(stateWithoutFlag).Should().BeEmpty();
        manager.GetAvailableActivities(stateWithFlag).Select(a => a.Id).Should().Contain(FlaggedActivity.Id);
    }

    private static StatBlock DefaultStats =>
        new(Money: 1000, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10) { MaxStamina = 50, CurrentStamina = 50 };

    private static GameState MakeState(StatBlock stats, int age = 20) => new()
    {
        PlayerId = "test",
        EraId = "2024plus",
        Age = age,
        Stats = stats,
    };

    private static EraConfig MakeEra(string eraId, decimal multiplier) => new(
        EraId: eraId,
        StartYear: 2000,
        EndYear: 2020,
        InflationMultiplier: multiplier,
        AverageHousePrice: 1,
        StartingMoney: 1,
        AvailableCareerTracks: ["tech"],
        EventPoolFiles: ["events_test.json"]);
}
