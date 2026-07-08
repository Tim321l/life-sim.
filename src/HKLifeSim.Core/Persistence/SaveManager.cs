using System.Text.Json;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Persistence;

public sealed class SaveManager(ISaveStore store, TimeProvider timeProvider)
{
    public async Task SaveAsync(GameState state, string slot, CancellationToken cancellationToken = default)
    {
        var envelope = new SaveEnvelope
        {
            SchemaVersion = SaveEnvelope.CurrentSchemaVersion,
            SavedAtUtc = timeProvider.GetUtcNow(),
            State = state,
        };

        var json = JsonSerializer.Serialize(envelope, HkJsonContext.Default.SaveEnvelope);
        await store.WriteAsync(slot, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameState?> LoadAsync(string slot, CancellationToken cancellationToken = default)
    {
        var json = await store.ReadAsync(slot, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize(json, HkJsonContext.Default.SaveEnvelope)
            ?? throw new InvalidOperationException($"Save slot '{slot}' contained invalid or empty JSON.");

        if (envelope.SchemaVersion > SaveEnvelope.CurrentSchemaVersion)
        {
            throw new SaveVersionException(envelope.SchemaVersion, SaveEnvelope.CurrentSchemaVersion);
        }

        return envelope.State;
    }
}
