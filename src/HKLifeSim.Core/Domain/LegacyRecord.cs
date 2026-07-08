namespace HKLifeSim.Core.Domain;

public sealed record LegacyRecord(
    string SourcePlayerId,
    int InheritedMoney,
    IReadOnlyList<string> InheritedFlags,
    int FamilyReputationCarryOver);
