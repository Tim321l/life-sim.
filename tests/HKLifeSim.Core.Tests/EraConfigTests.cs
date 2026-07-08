using FluentAssertions;
using HKLifeSim.Core.Domain;

namespace HKLifeSim.Core.Tests;

public sealed class EraConfigTests
{
    private static readonly EraConfig Era2024Plus = new(
        EraId: "2024plus",
        StartYear: 2024,
        EndYear: 2045,
        InflationMultiplier: 1.0m,
        AverageHousePrice: 8_000_000m,
        StartingMoney: 20_000,
        AvailableCareerTracks: ["tech", "finance"],
        EventPoolFiles: ["events_2024plus.json"]);

    [Theory]
    [InlineData(2024, true)]
    [InlineData(2045, true)]
    [InlineData(2030, true)]
    [InlineData(2023, false)]
    [InlineData(2046, false)]
    public void Contains_Is_Inclusive_Of_Start_And_End_Year(int year, bool expected)
    {
        Era2024Plus.Contains(year).Should().Be(expected);
    }
}
