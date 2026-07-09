using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Systems;

public static class LegacySystem
{
    private const string LegacyFlagPrefix = "legacy_";

    public static LegacyRecord GenerateLegacy(GameState finalState, decimal inheritanceRate = 0.5m)
    {
        ArgumentNullException.ThrowIfNull(finalState);

        var rawInheritedMoney = Math.Floor(finalState.Stats.Money * inheritanceRate);
        var inheritedMoney = (int)Math.Max(0m, rawInheritedMoney);

        var inheritedFlags = finalState.FlagsSet
            .Where(flag => flag.StartsWith(LegacyFlagPrefix, StringComparison.Ordinal))
            .ToList();

        return new LegacyRecord(
            SourcePlayerId: finalState.PlayerId,
            SourceEraId: finalState.EraId,
            InheritedMoney: inheritedMoney,
            InheritedFlags: inheritedFlags,
            FamilyReputationCarryOver: finalState.Stats.Reputation / 2);
    }
}
