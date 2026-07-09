using FluentAssertions;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Persistence;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class FullLifeSimulationTests : IDisposable
{
    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly EraConfig Era = LoadEra();
    private static readonly IReadOnlyList<GameEvent> EventPool = LoadEvents();

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"hklifesim-fulllife-tests-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void AutoPlayedLife_Terminates_By_Age_100_And_Never_Exceeds_Stat_Bounds(int seed)
    {
        var state = PlayFullLife(seed, out _);

        state.Age.Should().BeLessThanOrEqualTo(100);
        state.IsAlive.Should().BeFalse();
        state.DeathCause.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AutoPlayedLife_Save_File_Round_Trips_Validly_At_An_Arbitrary_Mid_Life_Point()
    {
        var store = new FileSaveStore(_tempDirectory);
        var manager = new SaveManager(store, TimeProvider.System);

        var engine = new EventEngine(EventPool, seed: 2024);
        var lifecycle = new LifecycleSystem(seed: 2024);
        var state = CreateStartingState(seed: 2024);

        for (var turn = 0; turn < 10 && state.IsAlive; turn++)
        {
            var evt = engine.SelectNextEvent(state);
            engine.ApplyChoice(state, evt, engine.PickRandomChoiceId(evt));
            if (state.IsAlive)
            {
                lifecycle.AdvanceYear(state, Era);
            }
        }

        await manager.SaveAsync(state, "mid-life", TestContext.Current.CancellationToken);
        var loaded = await manager.LoadAsync("mid-life", TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.Age.Should().Be(state.Age);
        loaded.Stats.Should().Be(state.Stats);
        loaded.FlagsSet.Should().BeEquivalentTo(state.FlagsSet);
        loaded.EventHistory.Should().Equal(state.EventHistory);
    }

    [Fact]
    public async Task AutoPlayedLife_Save_File_Round_Trips_Validly_After_Death()
    {
        var store = new FileSaveStore(_tempDirectory);
        var manager = new SaveManager(store, TimeProvider.System);

        var state = PlayFullLife(seed: 555, out _);

        await manager.SaveAsync(state, "end-of-life", TestContext.Current.CancellationToken);
        var loaded = await manager.LoadAsync("end-of-life", TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.IsAlive.Should().BeFalse();
        loaded.DeathCause.Should().Be(state.DeathCause);
        loaded.Age.Should().Be(state.Age);
    }

    [Fact]
    public Task AutoPlayedLife_Seed42_Matches_Snapshot()
    {
        var state = PlayFullLife(seed: 42, out _);

        var snapshot = new
        {
            state.Age,
            state.CurrentYear,
            state.IsAlive,
            state.DeathCause,
            state.Stats,
            FlagsSet = state.FlagsSet.OrderBy(f => f, StringComparer.Ordinal),
            state.EventHistory,
        };

        return Verifier.Verify(snapshot);
    }

    public static IEnumerable<object[]> Seeds()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            yield return [seed];
        }
    }

    private static GameState PlayFullLife(int seed, out int turns)
    {
        var engine = new EventEngine(EventPool, seed);
        var lifecycle = new LifecycleSystem(seed);
        var state = CreateStartingState(seed);

        turns = 0;
        while (state.IsAlive)
        {
            var evt = engine.SelectNextEvent(state);
            engine.ApplyChoice(state, evt, engine.PickRandomChoiceId(evt));
            AssertBoundsHold(state.Stats);
            turns++;

            if (!state.IsAlive)
            {
                break;
            }

            lifecycle.AdvanceYear(state, Era);
            AssertBoundsHold(state.Stats);
        }

        return state;
    }

    private static GameState CreateStartingState(int seed) => new()
    {
        PlayerId = "test-player",
        EraId = Era.EraId,
        Age = 18,
        CurrentYear = Era.StartYear,
        Stats = StatBlock.CreateStarting(Era, legacy: null),
        RngSeed = seed,
    };

    private static void AssertBoundsHold(StatBlock stats)
    {
        stats.Health.Should().BeInRange(0, 100);
        stats.Stress.Should().BeInRange(0, 100);
        stats.FamilyBond.Should().BeInRange(0, 100);
        stats.Education.Should().BeInRange(0, 100);
        stats.Reputation.Should().BeInRange(0, 100);
    }

    private static EraConfig LoadEra()
    {
        var json = File.ReadAllText(Path.Combine(DataDirectory, "eras.json"));
        return EraRepository.Load(json).Single(e => e.EraId == "2024plus");
    }

    private static IReadOnlyList<GameEvent> LoadEvents()
    {
        var files = Era.EventPoolFiles.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(DataDirectory, f)));
        return EventRepository.Load(files, [Era]);
    }
}
