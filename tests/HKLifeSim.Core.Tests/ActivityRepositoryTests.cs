using FluentAssertions;
using HKLifeSim.Core.Data;

namespace HKLifeSim.Core.Tests;

public sealed class ActivityRepositoryTests
{
    [Fact]
    public void Load_Throws_On_Malformed_Json()
    {
        const string json = "{not valid json";

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*invalid JSON*");
    }

    [Fact]
    public void Load_Throws_When_Activities_Property_Is_Explicitly_Null()
    {
        const string json = """{"schemaVersion":1,"activities":null}""";

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*no activities array*");
    }

    [Fact]
    public void Load_Throws_When_Activities_Property_Is_Missing_Entirely()
    {
        const string json = """{"schemaVersion":1}""";

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*invalid JSON*");
    }

    [Fact]
    public void Load_Throws_On_Duplicate_Activity_Ids()
    {
        var json = WrapFile(MakeActivityJson("dup"), MakeActivityJson("dup"));

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*duplicate*");
    }

    [Fact]
    public void Load_Throws_On_An_Empty_Id()
    {
        var json = WrapFile("""{"id":"","name":"n","type":"academic","staminaCost":5,"minAge":3,"maxAge":11}""");

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*empty id*");
    }

    [Fact]
    public void Load_Throws_On_An_Empty_Name()
    {
        var json = WrapFile("""{"id":"a","name":"","type":"academic","staminaCost":5,"minAge":3,"maxAge":11}""");

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*name*");
    }

    [Fact]
    public void Load_Throws_When_MinAge_Is_Greater_Than_MaxAge()
    {
        var json = WrapFile("""{"id":"a","name":"n","type":"academic","staminaCost":5,"minAge":30,"maxAge":11}""");

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*minAge*");
    }

    [Fact]
    public void Load_Throws_On_Negative_StaminaCost()
    {
        var json = WrapFile("""{"id":"a","name":"n","type":"academic","staminaCost":-1,"minAge":3,"maxAge":11}""");

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*staminaCost*");
    }

    [Fact]
    public void Load_Throws_On_Negative_MoneyCost()
    {
        var json = WrapFile("""{"id":"a","name":"n","type":"academic","staminaCost":5,"moneyCost":-1,"minAge":3,"maxAge":11}""");

        var act = () => ActivityRepository.Load(json);

        act.Should().Throw<EventDataException>().WithMessage("*moneyCost*");
    }

    [Fact]
    public void Load_Normalizes_Missing_RequiredFlags_To_Empty_Instead_Of_Null()
    {
        var json = WrapFile(MakeActivityJson("a"));

        var activities = ActivityRepository.Load(json);

        activities[0].RequiredFlags.Should().NotBeNull();
        activities[0].RequiredFlags.Should().BeEmpty();
    }

    [Fact]
    public void Load_Succeeds_For_The_Real_Activities_Data_File()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "activities.json"));

        var activities = ActivityRepository.Load(json);

        activities.Should().HaveCountGreaterThanOrEqualTo(8);
        activities.Should().AllSatisfy(a =>
        {
            a.Id.Should().NotBeNullOrWhiteSpace();
            a.Name.Should().NotBeNullOrWhiteSpace();
        });
        activities.Select(a => a.Id).Should().Contain([
            "simple_sketching",
            "double_dutch_skipping",
            "childrens_drama_performance",
            "literacy_competition",
            "study_revision",
            "tutoring_class",
            "drivers_license_test",
            "blind_date",
        ]);
    }

    private static string MakeActivityJson(string id) =>
        $$"""{"id":"{{id}}","name":"n","type":"academic","staminaCost":5,"minAge":3,"maxAge":11}""";

    private static string WrapFile(params string[] activities) =>
        $$"""{"schemaVersion":1,"activities":[{{string.Join(",", activities)}}]}""";
}
