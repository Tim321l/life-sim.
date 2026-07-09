using FluentAssertions;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;

namespace HKLifeSim.Core.Tests;

public sealed class EventEngineTests
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
    public void SelectNextEvent_Weighted_Selection_Converges_To_Expected_Ratio_Over_Many_Trials()
    {
        var eventA = MakeEvent("a", 18, 18, weight: 70);
        var eventB = MakeEvent("b", 18, 18, weight: 30);
        var engine = new EventEngine([eventA, eventB], TestEra, seed: 12345);
        var state = CreateState(age: 18);

        const int trials = 10_000;
        var countA = 0;
        for (var i = 0; i < trials; i++)
        {
            if (engine.SelectNextEvent(state).Id == "a")
            {
                countA++;
            }
        }

        var ratio = countA / (double)trials;
        ratio.Should().BeApproximately(0.7, 0.05);
    }

    [Fact]
    public void SelectNextEvent_Never_Repeats_A_Unique_Event()
    {
        var once = MakeEvent("once", 18, 18, weight: 100, isUnique: true);
        var fallback = MakeEvent("generic_daily_life_2024plus", 0, 120, weight: 1);
        var engine = new EventEngine([once, fallback], TestEra, seed: 1);
        var state = CreateState(age: 18);

        var first = engine.SelectNextEvent(state);
        first.Id.Should().Be("once");
        engine.ApplyChoice(state, first, first.Choices[0].Id);

        for (var i = 0; i < 20; i++)
        {
            engine.SelectNextEvent(state).Id.Should().NotBe("once");
        }
    }

    [Fact]
    public void SelectNextEvent_Excludes_Events_Whose_Conditions_Are_Not_Met()
    {
        var gated = MakeEvent("gated", 18, 18, weight: 100, conditions: [new EventCondition { Op = "hasFlag", Value = "married" }]);
        var fallback = MakeEvent("generic_daily_life_2024plus", 0, 120, weight: 1);
        var engine = new EventEngine([gated, fallback], TestEra, seed: 1);
        var state = CreateState(age: 18);

        engine.SelectNextEvent(state).Id.Should().Be("generic_daily_life_2024plus");

        state.SetFlag("married");

        engine.SelectNextEvent(state).Id.Should().Be("gated");
    }

    [Fact]
    public void SelectNextEvent_Returns_The_Fallback_When_No_Candidates_Match_The_Current_Age()
    {
        var narrow = MakeEvent("narrow", 40, 41, weight: 100);
        var fallback = MakeEvent("generic_daily_life_2024plus", 0, 120, weight: 1);
        var engine = new EventEngine([narrow, fallback], TestEra, seed: 1);
        var state = CreateState(age: 18);

        engine.SelectNextEvent(state).Id.Should().Be("generic_daily_life_2024plus");
    }

    [Fact]
    public void ApplyChoice_Queues_A_FollowUp_Event_That_Fires_On_The_Next_Selection()
    {
        var step2 = MakeEvent("step2", 18, 30, weight: 100, isUnique: true);
        var step1 = MakeEvent(
            "step1",
            18,
            18,
            weight: 100,
            isUnique: true,
            choices: [new EventChoice { Id = "go", Text = "go", FollowUpEventId = "step2" }]);
        var fallback = MakeEvent("generic_daily_life_2024plus", 0, 120, weight: 1);
        var engine = new EventEngine([step1, step2, fallback], TestEra, seed: 1);
        var state = CreateState(age: 18);

        var first = engine.SelectNextEvent(state);
        first.Id.Should().Be("step1");
        engine.ApplyChoice(state, first, "go");

        engine.SelectNextEvent(state).Id.Should().Be("step2");
    }

    [Fact]
    public void SelectNextEvent_Defers_A_FollowUp_Until_Its_Age_Window_Is_Reached()
    {
        var future = MakeEvent("future", 25, 30, weight: 100, isUnique: true);
        var trigger = MakeEvent(
            "trigger",
            18,
            18,
            weight: 100,
            isUnique: true,
            choices: [new EventChoice { Id = "go", Text = "go", FollowUpEventId = "future" }]);
        var fallback = MakeEvent("generic_daily_life_2024plus", 0, 120, weight: 1);
        var engine = new EventEngine([trigger, future, fallback], TestEra, seed: 1);
        var state = CreateState(age: 18);

        engine.ApplyChoice(state, engine.SelectNextEvent(state), "go");

        state.Age = 20;
        engine.SelectNextEvent(state).Id.Should().Be("generic_daily_life_2024plus");

        state.Age = 25;
        engine.SelectNextEvent(state).Id.Should().Be("future");
    }

    [Fact]
    public void ApplyChoice_Applies_Effects_Sets_Flags_And_Records_History()
    {
        var choice = new EventChoice
        {
            Id = "c1",
            Text = "t",
            Effects = new StatDelta(Money: 100, Stress: -5),
            FlagsToSet = ["foo"],
        };
        var evt = MakeEvent("e1", 18, 18, weight: 100, choices: [choice]);
        var engine = new EventEngine([evt], TestEra, seed: 1);
        var state = CreateState(age: 18);
        var before = state.Stats;

        engine.ApplyChoice(state, evt, "c1");

        state.Stats.Money.Should().Be(before.Money + 100);
        state.Stats.Stress.Should().Be(before.Stress - 5);
        state.HasFlag("foo").Should().BeTrue();
        state.EventHistory.Should().Contain("e1");
    }

    [Fact]
    public void ApplyChoice_Throws_For_An_Unknown_Choice_Id()
    {
        var evt = MakeEvent("e1", 18, 18, weight: 100);
        var engine = new EventEngine([evt], TestEra, seed: 1);
        var state = CreateState(age: 18);

        var act = () => engine.ApplyChoice(state, evt, "does-not-exist");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplyChoice_Marks_State_Dead_When_The_Resulting_Stats_Are_Fatal()
    {
        var choice = new EventChoice { Id = "c1", Text = "t", Effects = new StatDelta(Stress: 200) };
        var evt = MakeEvent("e1", 18, 18, weight: 100, choices: [choice]);
        var engine = new EventEngine([evt], TestEra, seed: 1);
        var state = CreateState(age: 18);

        engine.ApplyChoice(state, evt, "c1");

        state.IsAlive.Should().BeFalse();
        state.DeathCause.Should().Be("stress_breakdown");
    }

    [Fact]
    public void ApplyChoice_Scales_Money_By_The_Era_Inflation_Multiplier()
    {
        var lowMultiplierEra = TestEra with { InflationMultiplier = 0.02m };
        var choice = new EventChoice { Id = "c1", Text = "t", Effects = new StatDelta(Money: -100) };
        var evt = MakeEvent("e1", 18, 18, weight: 100, choices: [choice]);
        var engine = new EventEngine([evt], lowMultiplierEra, seed: 1);
        var state = CreateState(age: 18);
        var before = state.Stats;

        engine.ApplyChoice(state, evt, "c1");

        state.Stats.Money.Should().Be(before.Money - 2);
    }

    [Fact]
    public void ApplyChoice_Bypasses_Inflation_Scaling_When_The_Choice_Sets_AbsoluteMoney()
    {
        var lowMultiplierEra = TestEra with { InflationMultiplier = 0.02m };
        var choice = new EventChoice { Id = "c1", Text = "t", Effects = new StatDelta(Money: -100), AbsoluteMoney = true };
        var evt = MakeEvent("e1", 18, 18, weight: 100, choices: [choice]);
        var engine = new EventEngine([evt], lowMultiplierEra, seed: 1);
        var state = CreateState(age: 18);
        var before = state.Stats;

        engine.ApplyChoice(state, evt, "c1");

        state.Stats.Money.Should().Be(before.Money - 100);
    }

    [Fact]
    public void PickRandomChoiceId_Returns_One_Of_The_Event_Choice_Ids()
    {
        var evt = MakeEvent(
            "e1",
            18,
            18,
            weight: 100,
            choices: [new EventChoice { Id = "a", Text = "a" }, new EventChoice { Id = "b", Text = "b" }]);
        var engine = new EventEngine([evt], TestEra, seed: 1);

        var picked = engine.PickRandomChoiceId(evt);

        picked.Should().BeOneOf("a", "b");
    }

    private static GameState CreateState(int age) => new()
    {
        PlayerId = "player-1",
        EraId = "2024plus",
        Age = age,
        Stats = new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 10, Reputation: 10),
    };

    private static GameEvent MakeEvent(
        string id,
        int minAge,
        int maxAge,
        int weight,
        bool isUnique = false,
        IReadOnlyList<EventCondition>? conditions = null,
        IReadOnlyList<EventChoice>? choices = null) => new()
    {
        Id = id,
        EraId = "2024plus",
        MinAge = minAge,
        MaxAge = maxAge,
        Weight = weight,
        IsUnique = isUnique,
        Category = "test",
        Title = "title",
        Body = "body",
        Conditions = conditions ?? [],
        Choices = choices ?? [new EventChoice { Id = "only", Text = "only" }],
    };
}
