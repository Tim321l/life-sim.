using System.Text.Json;
using FluentAssertions;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Persistence;

namespace HKLifeSim.Core.Tests;

public sealed class SaveRoundTripTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"hklifesim-tests-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Save_Then_Load_Round_Trips_A_Fully_Populated_GameState_With_Value_Equality()
    {
        var store = new FileSaveStore(_tempDirectory);
        var manager = new SaveManager(store, TimeProvider.System);

        var original = new GameState
        {
            PlayerId = "player-1",
            EraId = "2024plus",
            Age = 34,
            CurrentYear = 2058,
            Stats = new StatBlock(Money: -12_345, Health: 42, Stress: 67, FamilyBond: 88, Education: 90, Reputation: 15),
            RngSeed = 42,
            IsAlive = false,
            DeathCause = "stress_breakdown",
            Profile = new CharacterProfile("陳大文", Gender.Male, BirthYear: 2024),
            InheritedLegacy = new LegacyRecord(
                SourcePlayerId: "player-0",
                InheritedMoney: 3_000,
                InheritedFlags: ["legacy_owns_flat", "legacy_emigrated"],
                FamilyReputationCarryOver: 20),
        };
        original.SetFlag("married");
        original.SetFlag("legacy_owns_flat");
        original.EventHistory.Add("dse_results_2024plus");
        original.EventHistory.Add("first_internship_2024plus");

        await manager.SaveAsync(original, "slot-a", TestContext.Current.CancellationToken);
        var loaded = await manager.LoadAsync("slot-a", TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.PlayerId.Should().Be(original.PlayerId);
        loaded.EraId.Should().Be(original.EraId);
        loaded.Age.Should().Be(original.Age);
        loaded.CurrentYear.Should().Be(original.CurrentYear);
        loaded.Stats.Should().Be(original.Stats);
        loaded.RngSeed.Should().Be(original.RngSeed);
        loaded.IsAlive.Should().Be(original.IsAlive);
        loaded.DeathCause.Should().Be(original.DeathCause);
        loaded.Profile.Should().Be(original.Profile);
        loaded.InheritedLegacy.Should().BeEquivalentTo(original.InheritedLegacy);
        loaded.FlagsSet.Should().BeEquivalentTo(original.FlagsSet);
        loaded.EventHistory.Should().Equal(original.EventHistory);
    }

    [Fact]
    public async Task Load_Returns_Null_When_Slot_Does_Not_Exist()
    {
        var store = new FileSaveStore(_tempDirectory);
        var manager = new SaveManager(store, TimeProvider.System);

        var loaded = await manager.LoadAsync("does-not-exist", TestContext.Current.CancellationToken);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_Leaves_No_Tmp_File_Behind_After_A_Successful_Write()
    {
        var store = new FileSaveStore(_tempDirectory);

        await store.WriteAsync("slot-a", """{"hello":"world"}""", TestContext.Current.CancellationToken);

        File.Exists(Path.Combine(_tempDirectory, "slot-a.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDirectory, "slot-a.json.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("nested/slot")]
    [InlineData("nested\\slot")]
    public async Task WriteAsync_Rejects_Invalid_Slot_Names(string slot)
    {
        var store = new FileSaveStore(_tempDirectory);

        var act = async () => await store.WriteAsync(slot, "{}");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_Rejects_Slot_Names_Longer_Than_SixtyFour_Characters()
    {
        var store = new FileSaveStore(_tempDirectory);
        var tooLong = new string('a', 65);

        var act = async () => await store.WriteAsync(tooLong, "{}");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_Throws_SaveVersionException_For_An_Unknown_Higher_SchemaVersion()
    {
        var store = new FileSaveStore(_tempDirectory);
        var manager = new SaveManager(store, TimeProvider.System);

        var futureEnvelope = new SaveEnvelope
        {
            SchemaVersion = 99,
            SavedAtUtc = DateTimeOffset.UtcNow,
            State = new GameState { PlayerId = "player-1", EraId = "2024plus" },
        };
        var json = JsonSerializer.Serialize(futureEnvelope, HkJsonContext.Default.SaveEnvelope);
        await store.WriteAsync("future-slot", json, TestContext.Current.CancellationToken);

        var act = async () => await manager.LoadAsync("future-slot");

        await act.Should().ThrowAsync<SaveVersionException>();
    }
}
