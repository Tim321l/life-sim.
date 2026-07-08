namespace HKLifeSim.Core.Domain;

public sealed record EraConfig(
    string EraId,
    int StartYear,
    int EndYear,
    decimal InflationMultiplier,
    decimal AverageHousePrice,
    int StartingMoney,
    IReadOnlyList<string> AvailableCareerTracks,
    IReadOnlyList<string> EventPoolFiles)
{
    public bool Contains(int year) => year >= StartYear && year <= EndYear;
}
