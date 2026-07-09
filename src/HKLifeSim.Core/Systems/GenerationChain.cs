using System.Diagnostics.CodeAnalysis;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Systems;

public sealed class GenerationChain
{
    private readonly IReadOnlyList<EraConfig> _eras;

    public GenerationChain(IReadOnlyList<EraConfig> eras)
    {
        ArgumentNullException.ThrowIfNull(eras);

        _eras = eras;
    }

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Lineage grows one LegacyRecord per completed generation and is replaced wholesale on load; a settable list keeps that simple.")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Lineage is an ordered, append-only log of ancestor LegacyRecords; List<LegacyRecord> is the domain type per spec and round-trips through JSON as-is.")]
    public List<LegacyRecord> Lineage { get; set; } = [];

    public GameState StartNextGeneration(EraConfig targetEra, int seed)
    {
        ArgumentNullException.ThrowIfNull(targetEra);

        LegacyRecord? legacy = null;

        if (Lineage.Count > 0)
        {
            var previous = Lineage[^1];
            var sourceEra = _eras.FirstOrDefault(e => e.EraId == previous.SourceEraId)
                ?? throw new InvalidOperationException($"Unknown source era '{previous.SourceEraId}' referenced by the lineage.");

            if (targetEra.StartYear < sourceEra.StartYear)
            {
                throw new ArgumentException(
                    $"Target era '{targetEra.EraId}' ({targetEra.StartYear}) precedes source era '{sourceEra.EraId}' ({sourceEra.StartYear}). A new generation must start in the same era or later.",
                    nameof(targetEra));
            }

            legacy = ConvertToEraScale(previous, sourceEra, targetEra);
        }

        var state = new GameState
        {
            PlayerId = Guid.NewGuid().ToString("N"),
            EraId = targetEra.EraId,
            Age = 6,
            CurrentYear = targetEra.StartYear,
            Stats = StatBlock.CreateStarting(targetEra, legacy),
            RngSeed = seed,
            InheritedLegacy = legacy,
        };

        if (legacy is not null)
        {
            foreach (var flag in legacy.InheritedFlags)
            {
                state.SetFlag(flag);
            }
        }

        return state;
    }

    private static LegacyRecord ConvertToEraScale(LegacyRecord legacy, EraConfig sourceEra, EraConfig targetEra)
    {
        if (string.Equals(sourceEra.EraId, targetEra.EraId, StringComparison.Ordinal))
        {
            return legacy;
        }

        var convertedMoney = (int)Math.Round(
            legacy.InheritedMoney * (targetEra.InflationMultiplier / sourceEra.InflationMultiplier),
            MidpointRounding.AwayFromZero);

        return legacy with { InheritedMoney = convertedMoney };
    }
}
