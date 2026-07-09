namespace HKLifeSim.Core.Domain;

public sealed record LegacyRecord(
    string SourcePlayerId,
    string SourceEraId,
    int InheritedMoney,
    IReadOnlyList<string> InheritedFlags,
    int FamilyReputationCarryOver);
