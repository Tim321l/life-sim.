using System.Text.Json;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Data;

public static class EraRepository
{
    public static IReadOnlyList<EraConfig> Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        EraFile file;
        try
        {
            file = JsonSerializer.Deserialize(json, HkJsonContext.Default.EraFile)
                ?? throw new EventDataException("eras.json: file is empty.");
        }
        catch (JsonException ex)
        {
            throw new EventDataException($"eras.json: invalid JSON — {ex.Message}", ex);
        }

        var eras = file.Eras ?? throw new EventDataException("eras.json: file has no eras array.");

        foreach (var era in eras)
        {
            if (string.IsNullOrWhiteSpace(era.EraId))
            {
                throw new EventDataException("eras.json: an era is missing eraId.");
            }

            if (era.StartYear > era.EndYear)
            {
                throw new EventDataException($"eras.json/{era.EraId}: startYear ({era.StartYear}) > endYear ({era.EndYear}).");
            }
        }

        return eras;
    }
}
