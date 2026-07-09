using FluentAssertions;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class GenerationChainTests
{
    private static readonly EraConfig Era1960s = MakeEra("1960s", 1960, 1979, 0.02m, 300);
    private static readonly EraConfig Era1980s = MakeEra("1980s", 1980, 1999, 0.15m, 3_000);
    private static readonly EraConfig Era2000s = MakeEra("2000s", 2000, 2023, 0.6m, 10_000);
    private static readonly EraConfig Era2024Plus = MakeEra("2024plus", 2024, 2045, 1.0m, 20_000);

    private static readonly IReadOnlyList<EraConfig> AllEras = [Era1960s, Era1980s, Era2000s, Era2024Plus];

    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly IReadOnlyList<EraConfig> RealEras = EraRepository.Load(File.ReadAllText(Path.Combine(DataDirectory, "eras.json")));

    [Fact]
    public void StartNextGeneration_With_Empty_Lineage_Starts_A_Fresh_Life_With_No_Legacy()
    {
        var chain = new GenerationChain(AllEras);

        var state = chain.StartNextGeneration(Era1960s, seed: 1);

        state.Age.Should().Be(6);
        state.EraId.Should().Be("1960s");
        state.Stats.Money.Should().Be(300);
        state.InheritedLegacy.Should().BeNull();
        state.FlagsSet.Should().BeEmpty();
    }

    [Fact]
    public void StartNextGeneration_Converts_Inherited_Money_Across_Eras_By_The_Multiplier_Ratio()
    {
        var chain = new GenerationChain(AllEras)
        {
            Lineage = [MakeLegacy("1960s", inheritedMoney: 100)],
        };

        var state = chain.StartNextGeneration(Era2024Plus, seed: 1);

        // 2024plus multiplier / 1960s multiplier = 1.0 / 0.02 = 50x.
        state.InheritedLegacy!.InheritedMoney.Should().Be(5_000);
        state.Stats.Money.Should().Be(20_000 + 5_000);
    }

    [Fact]
    public void StartNextGeneration_Is_The_Identity_Conversion_Within_The_Same_Era()
    {
        var chain = new GenerationChain(AllEras)
        {
            Lineage = [MakeLegacy("2024plus", inheritedMoney: 1_234)],
        };

        var state = chain.StartNextGeneration(Era2024Plus, seed: 1);

        state.InheritedLegacy!.InheritedMoney.Should().Be(1_234);
    }

    [Fact]
    public void StartNextGeneration_PreSets_Inherited_Flags_On_The_New_State()
    {
        var chain = new GenerationChain(AllEras)
        {
            Lineage = [MakeLegacy("1960s", inheritedMoney: 0, flags: ["legacy_owns_flat", "legacy_emigrated"])],
        };

        var state = chain.StartNextGeneration(Era1980s, seed: 1);

        state.HasFlag("legacy_owns_flat").Should().BeTrue();
        state.HasFlag("legacy_emigrated").Should().BeTrue();
    }

    [Fact]
    public void StartNextGeneration_Folds_Reputation_Carry_Over_Into_Starting_Reputation()
    {
        var chain = new GenerationChain(AllEras)
        {
            Lineage = [MakeLegacy("1960s", inheritedMoney: 0, reputationCarryOver: 15)],
        };

        var state = chain.StartNextGeneration(Era1980s, seed: 1);

        state.Stats.Reputation.Should().Be(25);
    }

    [Fact]
    public void StartNextGeneration_Throws_When_The_Target_Era_Precedes_The_Source_Era()
    {
        var chain = new GenerationChain(AllEras)
        {
            Lineage = [MakeLegacy("2000s", inheritedMoney: 0)],
        };

        var act = () => chain.StartNextGeneration(Era1980s, seed: 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StartNextGeneration_Allows_Starting_The_Next_Generation_In_The_Same_Era()
    {
        var chain = new GenerationChain(AllEras)
        {
            Lineage = [MakeLegacy("1980s", inheritedMoney: 0)],
        };

        var act = () => chain.StartNextGeneration(Era1980s, seed: 1);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_Throws_For_Null_Eras()
    {
        var act = () => new GenerationChain(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThreeGeneration_AutoRun_From_1960s_Through_1980s_To_2024plus_Completes_Without_Exceptions()
    {
        var chain = new GenerationChain(RealEras);
        var eraSequence = new[] { "1960s", "1980s", "2024plus" };

        var seed = 7;
        foreach (var eraId in eraSequence)
        {
            var era = RealEras.Single(e => e.EraId == eraId);
            var state = chain.StartNextGeneration(era, seed);
            state = PlayToDeath(state, era, seed);

            state.IsAlive.Should().BeFalse();
            state.DeathCause.Should().NotBeNullOrEmpty();

            chain.Lineage.Add(LegacySystem.GenerateLegacy(state));
            seed++;
        }

        chain.Lineage.Should().HaveCount(3);
    }

    private static GameState PlayToDeath(GameState state, EraConfig era, int seed)
    {
        var events = LoadEventsFor(era);
        var engine = new EventEngine(events, era, seed);
        var lifecycle = new LifecycleSystem(seed);

        while (state.IsAlive)
        {
            var evt = engine.SelectNextEvent(state);
            engine.ApplyChoice(state, evt, engine.PickRandomChoiceId(evt));

            if (!state.IsAlive)
            {
                break;
            }

            lifecycle.AdvanceYear(state, era);
        }

        return state;
    }

    private static IReadOnlyList<GameEvent> LoadEventsFor(EraConfig era)
    {
        var files = era.EventPoolFiles.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(DataDirectory, f)));
        return EventRepository.Load(files, [era]);
    }

    private static LegacyRecord MakeLegacy(
        string sourceEraId,
        int inheritedMoney,
        IReadOnlyList<string>? flags = null,
        int reputationCarryOver = 0) => new(
        SourcePlayerId: "parent-1",
        SourceEraId: sourceEraId,
        InheritedMoney: inheritedMoney,
        InheritedFlags: flags ?? [],
        FamilyReputationCarryOver: reputationCarryOver);

    private static EraConfig MakeEra(string eraId, int startYear, int endYear, decimal multiplier, int startingMoney) => new(
        EraId: eraId,
        StartYear: startYear,
        EndYear: endYear,
        InflationMultiplier: multiplier,
        AverageHousePrice: 1,
        StartingMoney: startingMoney,
        AvailableCareerTracks: ["tech"],
        EventPoolFiles: [$"events_{eraId}.json"]);
}
