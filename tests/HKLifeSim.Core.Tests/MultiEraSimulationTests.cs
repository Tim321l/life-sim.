using FluentAssertions;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class MultiEraSimulationTests
{
    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly IReadOnlyList<EraConfig> Eras = EraRepository.Load(File.ReadAllText(Path.Combine(DataDirectory, "eras.json")));

    [Theory]
    [MemberData(nameof(EraAndSeedCombinations))]
    public void AutoPlayedLife_Completes_Without_Exceptions_In_Every_Era(string eraId, int seed)
    {
        var era = Eras.Single(e => e.EraId == eraId);
        var events = LoadEventsFor(era);
        var engine = new EventEngine(events, era, seed);
        var lifecycle = new LifecycleSystem(seed);

        var state = new GameState
        {
            PlayerId = "test-player",
            EraId = era.EraId,
            Age = 18,
            CurrentYear = era.StartYear,
            Stats = StatBlock.CreateStarting(era, legacy: null),
            RngSeed = seed,
        };

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

        state.Age.Should().BeLessThanOrEqualTo(100);
        state.DeathCause.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Loader_Accepts_All_Four_Era_Files_Together_With_Globally_Unique_Ids()
    {
        var files = Eras
            .SelectMany(era => era.EventPoolFiles)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(f => f, f => File.ReadAllText(Path.Combine(DataDirectory, f)));

        var events = EventRepository.Load(files, Eras);

        events.Should().HaveCountGreaterThan(0);
        events.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        Eras.Select(e => e.EraId).Should().OnlyHaveUniqueItems();
    }

    public static IEnumerable<object[]> EraAndSeedCombinations()
    {
        foreach (var eraId in new[] { "1960s", "1980s", "2000s", "2024plus" })
        {
            for (var seed = 1; seed <= 25; seed++)
            {
                yield return [eraId, seed];
            }
        }
    }

    private static IReadOnlyList<GameEvent> LoadEventsFor(EraConfig era)
    {
        var files = era.EventPoolFiles.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(DataDirectory, f)));
        return EventRepository.Load(files, [era]);
    }
}
