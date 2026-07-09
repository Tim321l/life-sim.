using FluentAssertions;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;

namespace HKLifeSim.Core.Tests;

public sealed class EventRepositoryTests
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
    public void Load_Throws_On_Duplicate_Event_Ids()
    {
        var json = WrapFile("2024plus", MakeEventJson("dup"), MakeEventJson("dup"), MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*duplicate*");
    }

    [Fact]
    public void Load_Throws_On_Dangling_FollowUpEventId()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","choices":[{"id":"c1","text":"t","followUpEventId":"does_not_exist"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*followUpEventId*");
    }

    [Fact]
    public void Load_Throws_When_A_FollowUp_Only_Exists_In_A_Different_Era()
    {
        var otherEra = TestEra with { EraId = "other" };
        var evtJsonA = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","choices":[{"id":"c1","text":"t","followUpEventId":"b"}]}""";
        var fileA = WrapFile("2024plus", evtJsonA, MakeEventJson("generic_daily_life_2024plus"));
        var fileB = WrapFile("other", MakeEventJson("b"), MakeEventJson("generic_daily_life_other"));

        var act = () => EventRepository.Load(
            new Dictionary<string, string> { ["a.json"] = fileA, ["b.json"] = fileB },
            [TestEra, otherEra]);

        act.Should().Throw<EventDataException>().WithMessage("*followUpEventId*");
    }

    [Fact]
    public void Load_Throws_On_Unknown_Condition_Operator()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","conditions":[{"op":"~=","statName":"Stress","value":"10"}],"choices":[{"id":"c1","text":"t"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*condition op*");
    }

    [Fact]
    public void Load_Throws_On_Stat_Condition_With_An_Invalid_StatName()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","conditions":[{"op":">=","statName":"Wisdom","value":"10"}],"choices":[{"id":"c1","text":"t"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*statName*");
    }

    [Fact]
    public void Load_Throws_On_Stat_Condition_With_A_Non_Integer_Value()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","conditions":[{"op":">=","statName":"Stress","value":"abc"}],"choices":[{"id":"c1","text":"t"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*integer*");
    }

    [Fact]
    public void Load_Throws_On_A_Flag_Condition_With_An_Empty_Value()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","conditions":[{"op":"hasFlag","value":""}],"choices":[{"id":"c1","text":"t"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*non-empty*");
    }

    [Fact]
    public void Load_Throws_When_MinAge_Is_Greater_Than_MaxAge()
    {
        var evtJson = """{"id":"a","minAge":30,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","choices":[{"id":"c1","text":"t"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*minAge*");
    }

    [Fact]
    public void Load_Throws_On_Negative_Weight()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":-1,"category":"test","title":"t","body":"b","choices":[{"id":"c1","text":"t"}]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*weight*");
    }

    [Fact]
    public void Load_Throws_When_An_Event_Has_No_Choices()
    {
        var evtJson = """{"id":"a","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","choices":[]}""";
        var json = WrapFile("2024plus", evtJson, MakeEventJson("generic_daily_life_2024plus"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*at least 1 choice*");
    }

    [Fact]
    public void Load_Throws_When_An_Era_Is_Missing_Its_Mandatory_Fallback_Event()
    {
        var json = WrapFile("2024plus", MakeEventJson("a"));

        var act = () => Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*fallback*");
    }

    [Fact]
    public void Load_Succeeds_For_A_Well_Formed_Minimal_Pool()
    {
        var json = WrapFile("2024plus", MakeEventJson("a"), MakeEventJson("generic_daily_life_2024plus"));

        var events = Load(json);

        events.Should().HaveCount(2);
        events.Should().AllSatisfy(e => e.EraId.Should().Be("2024plus"));
    }

    [Fact]
    public void Load_Normalizes_Missing_Optional_Collections_To_Empty_Instead_Of_Null()
    {
        var json = WrapFile("2024plus", MakeEventJson("a"), MakeEventJson("generic_daily_life_2024plus"));

        var events = Load(json);

        var evt = events.Single(e => e.Id == "a");
        evt.Conditions.Should().NotBeNull();
        evt.Conditions.Should().BeEmpty();
        evt.Choices[0].FlagsToSet.Should().NotBeNull();
        evt.Choices[0].FlagsToSet.Should().BeEmpty();
    }

    private static IReadOnlyList<GameEvent> Load(string json) =>
        EventRepository.Load(new Dictionary<string, string> { ["f.json"] = json }, [TestEra]);

    private static string MakeEventJson(string id) =>
        $$"""{"id":"{{id}}","minAge":18,"maxAge":18,"weight":10,"category":"test","title":"t","body":"b","choices":[{"id":"c1","text":"t"}]}""";

    private static string WrapFile(string eraId, params string[] events) =>
        $$"""{"schemaVersion":1,"eraId":"{{eraId}}","events":[{{string.Join(",", events)}}]}""";
}
