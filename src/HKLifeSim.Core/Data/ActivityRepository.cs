using System.Text.Json;
using HKLifeSim.Core.Activities;

namespace HKLifeSim.Core.Data;

public static class ActivityRepository
{
    public static IReadOnlyList<Activity> Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var file = Deserialize(json);
        var activities = (file.Activities ?? throw new EventDataException("activities.json: file has no activities array."))
            .Select(Normalize)
            .ToList();

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in activities)
        {
            ValidateActivity(activity);

            if (!seenIds.Add(activity.Id))
            {
                throw new EventDataException($"activities.json/{activity.Id}: duplicate activity id.");
            }
        }

        return activities;
    }

    private static ActivityFile Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, HkJsonContext.Default.ActivityFile)
                ?? throw new EventDataException("activities.json: file is empty.");
        }
        catch (JsonException ex)
        {
            throw new EventDataException($"activities.json: invalid JSON — {ex.Message}", ex);
        }
    }

    // Mirrors EventRepository.Normalize: source-generated JSON deserialization leaves an optional
    // init-only collection property null (not its C# field-initializer default) when the JSON key
    // is absent, so RequiredFlags is normalized back to an empty collection here.
    private static Activity Normalize(Activity rawActivity) =>
        rawActivity with { RequiredFlags = rawActivity.RequiredFlags ?? [] };

    private static void ValidateActivity(Activity activity)
    {
        if (string.IsNullOrWhiteSpace(activity.Id))
        {
            throw new EventDataException("activities.json: an activity has an empty id.");
        }

        if (string.IsNullOrWhiteSpace(activity.Name))
        {
            throw new EventDataException($"activities.json/{activity.Id}: name must not be empty.");
        }

        if (activity.MinAge > activity.MaxAge)
        {
            throw new EventDataException($"activities.json/{activity.Id}: minAge ({activity.MinAge}) > maxAge ({activity.MaxAge}).");
        }

        if (activity.StaminaCost < 0)
        {
            throw new EventDataException($"activities.json/{activity.Id}: staminaCost must be >= 0.");
        }

        if (activity.MoneyCost < 0)
        {
            throw new EventDataException($"activities.json/{activity.Id}: moneyCost must be >= 0.");
        }
    }
}
