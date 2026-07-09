using HKLifeSim.Core.Persistence;
using Microsoft.JSInterop;

namespace HKLifeSim.Web.Services;

internal sealed class LocalStorageSaveStore : ISaveStore, IAsyncDisposable
{
    private const int MaxSlotNameLength = 64;
    private const string KeyPrefix = "hklifesim_";

    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    public LocalStorageSaveStore(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);

        _moduleTask = new Lazy<Task<IJSObjectReference>>(() =>
            jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/localStorage.js").AsTask());
    }

    public async Task WriteAsync(string slot, string json, CancellationToken cancellationToken = default)
    {
        ValidateSlotName(slot);
        var module = await _moduleTask.Value.ConfigureAwait(false);
        await module.InvokeVoidAsync("setItem", cancellationToken, KeyFor(slot), json).ConfigureAwait(false);
    }

    public async Task<string?> ReadAsync(string slot, CancellationToken cancellationToken = default)
    {
        ValidateSlotName(slot);
        var module = await _moduleTask.Value.ConfigureAwait(false);
        return await module.InvokeAsync<string?>("getItem", cancellationToken, KeyFor(slot)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default)
    {
        var module = await _moduleTask.Value.ConfigureAwait(false);
        var keys = await module.InvokeAsync<string[]>("listKeysWithPrefix", cancellationToken, KeyPrefix).ConfigureAwait(false);
        return keys;
    }

    public async Task DeleteAsync(string slot, CancellationToken cancellationToken = default)
    {
        ValidateSlotName(slot);
        var module = await _moduleTask.Value.ConfigureAwait(false);
        await module.InvokeVoidAsync("removeItem", cancellationToken, KeyFor(slot)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string KeyFor(string slot) => $"{KeyPrefix}{slot}";

    private static void ValidateSlotName(string slot)
    {
        if (string.IsNullOrEmpty(slot))
        {
            throw new ArgumentException("Save slot name must not be empty.", nameof(slot));
        }

        if (slot.Length > MaxSlotNameLength)
        {
            throw new ArgumentException($"Save slot name must not exceed {MaxSlotNameLength} characters.", nameof(slot));
        }

        if (slot.Contains('/', StringComparison.Ordinal) ||
            slot.Contains('\\', StringComparison.Ordinal) ||
            slot.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Save slot name must not contain path separators or '..'.", nameof(slot));
        }
    }
}
