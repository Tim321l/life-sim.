using FluentAssertions;
using HKLifeSim.Core.Persistence;

namespace HKLifeSim.Core.Tests;

public sealed class FileSaveStoreTests : IDisposable
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
    public void Constructor_Creates_The_Base_Directory_When_Missing()
    {
        _ = new FileSaveStore(_tempDirectory);

        Directory.Exists(_tempDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task ListSlotsAsync_Returns_All_Written_Slot_Names()
    {
        var store = new FileSaveStore(_tempDirectory);
        await store.WriteAsync("slot-a", "{}", TestContext.Current.CancellationToken);
        await store.WriteAsync("slot-b", "{}", TestContext.Current.CancellationToken);

        var slots = await store.ListSlotsAsync(TestContext.Current.CancellationToken);

        slots.Should().BeEquivalentTo(["slot-a", "slot-b"]);
    }

    [Fact]
    public async Task ListSlotsAsync_Returns_Empty_When_No_Saves_Exist()
    {
        var store = new FileSaveStore(_tempDirectory);

        var slots = await store.ListSlotsAsync(TestContext.Current.CancellationToken);

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_Removes_An_Existing_Slot()
    {
        var store = new FileSaveStore(_tempDirectory);
        await store.WriteAsync("slot-a", "{}", TestContext.Current.CancellationToken);

        await store.DeleteAsync("slot-a", TestContext.Current.CancellationToken);

        (await store.ReadAsync("slot-a", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_Is_A_NoOp_When_The_Slot_Does_Not_Exist()
    {
        var store = new FileSaveStore(_tempDirectory);

        var act = async () => await store.DeleteAsync("does-not-exist", TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_Rejects_Invalid_Slot_Names()
    {
        var store = new FileSaveStore(_tempDirectory);

        var act = async () => await store.DeleteAsync("../escape", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadAsync_Rejects_Invalid_Slot_Names()
    {
        var store = new FileSaveStore(_tempDirectory);

        var act = async () => await store.ReadAsync("nested/slot", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
