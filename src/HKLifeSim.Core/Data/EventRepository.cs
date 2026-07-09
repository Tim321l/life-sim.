using System.Globalization;
using System.Text.Json;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;

namespace HKLifeSim.Core.Data;

public static class EventRepository
{
    public static IReadOnlyList<GameEvent> Load(IReadOnlyDictionary<string, string> fileNameToJson, IReadOnlyList<EraConfig> eras)
    {
        ArgumentNullException.ThrowIfNull(fileNameToJson);
        ArgumentNullException.ThrowIfNull(eras);

        var events = new List<GameEvent>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (fileName, json) in fileNameToJson)
        {
            var file = Deserialize(fileName, json);

            foreach (var rawEvent in file.Events ?? throw new EventDataException($"{fileName}: file has no events array."))
            {
                var evt = Normalize(rawEvent, file.EraId);
                ValidateEvent(fileName, evt);

                if (!seenIds.Add(evt.Id))
                {
                    throw new EventDataException($"{fileName}/{evt.Id}: duplicate event id across loaded files.");
                }

                events.Add(evt);
            }
        }

        ValidateFollowUpReferences(events);
        ValidateFallbacksPresent(events, eras);

        return events;
    }

    private static EventFile Deserialize(string fileName, string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, HkJsonContext.Default.EventFile)
                ?? throw new EventDataException($"{fileName}: file is empty.");
        }
        catch (JsonException ex)
        {
            throw new EventDataException($"{fileName}: invalid JSON — {ex.Message}", ex);
        }
    }

    // System.Text.Json source-generated deserialization leaves optional init-only collection
    // properties as null (not their C# field-initializer default) when the JSON key is absent.
    // Normalize those back to empty collections right here at the load boundary so the rest of
    // the codebase can treat GameEvent/EventChoice as fully non-null, as their types declare.
    private static GameEvent Normalize(GameEvent rawEvent, string eraId)
    {
        var choices = (rawEvent.Choices ?? []).Select(c => c with { FlagsToSet = c.FlagsToSet ?? [] }).ToList();

        return rawEvent with
        {
            EraId = eraId,
            Conditions = rawEvent.Conditions ?? [],
            Choices = choices,
        };
    }

    private static void ValidateEvent(string fileName, GameEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Id))
        {
            throw new EventDataException($"{fileName}: an event has an empty id.");
        }

        if (evt.MinAge > evt.MaxAge)
        {
            throw new EventDataException($"{fileName}/{evt.Id}: minAge ({evt.MinAge}) > maxAge ({evt.MaxAge}).");
        }

        if (evt.Weight < 0)
        {
            throw new EventDataException($"{fileName}/{evt.Id}: weight must be >= 0.");
        }

        if (evt.Choices.Count == 0)
        {
            throw new EventDataException($"{fileName}/{evt.Id}: must have at least 1 choice.");
        }

        foreach (var condition in evt.Conditions)
        {
            ValidateCondition(fileName, evt.Id, condition);
        }
    }

    private static void ValidateCondition(string fileName, string eventId, EventCondition condition)
    {
        if (!EventCondition.ValidOperators.Contains(condition.Op))
        {
            throw new EventDataException($"{fileName}/{eventId}: unknown condition op '{condition.Op}'.");
        }

        var isFlagOp = condition.Op is "hasFlag" or "notHasFlag";
        if (isFlagOp)
        {
            if (string.IsNullOrEmpty(condition.Value))
            {
                throw new EventDataException($"{fileName}/{eventId}: flag condition requires a non-empty value.");
            }

            return;
        }

        if (string.IsNullOrEmpty(condition.StatName) || !StatBlock.StatNames.Contains(condition.StatName))
        {
            throw new EventDataException($"{fileName}/{eventId}: condition op '{condition.Op}' requires a valid statName.");
        }

        if (!int.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            throw new EventDataException($"{fileName}/{eventId}: condition value '{condition.Value}' is not a valid integer.");
        }
    }

    private static void ValidateFollowUpReferences(IReadOnlyList<GameEvent> events)
    {
        var idsByEra = events
            .GroupBy(e => e.EraId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Id).ToHashSet(StringComparer.Ordinal));

        foreach (var evt in events)
        {
            var followUpIds = new[] { evt.FollowUpEventId }.Concat(evt.Choices.Select(c => c.FollowUpEventId));
            foreach (var followUpId in followUpIds)
            {
                if (followUpId is null)
                {
                    continue;
                }

                if (!idsByEra[evt.EraId].Contains(followUpId))
                {
                    throw new EventDataException($"{evt.Id}: followUpEventId '{followUpId}' does not exist in era '{evt.EraId}'.");
                }
            }
        }
    }

    private static void ValidateFallbacksPresent(IReadOnlyList<GameEvent> events, IReadOnlyList<EraConfig> eras)
    {
        foreach (var era in eras)
        {
            var fallbackId = $"generic_daily_life_{era.EraId}";
            var hasFallback = events.Any(e => e.Id == fallbackId && e.EraId == era.EraId);
            if (!hasFallback)
            {
                throw new EventDataException($"Era '{era.EraId}' is missing its mandatory fallback event '{fallbackId}'.");
            }
        }
    }
}
