using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Systems;

namespace HKLifeSim.Core.Tests;

public sealed class ExamSystemTests
{
    [Fact]
    public void AllocateSecondarySchool_ShouldSetBandingFlagsCorrectly()
    {
        // Arrange
        var stateLow = new GameState { PlayerId = "test", EraId = "2024plus", Age = 11, Stats = new StatBlock(1000, 80, 10, 50, 20, 10) };
        var stateMid = new GameState { PlayerId = "test", EraId = "2024plus", Age = 11, Stats = new StatBlock(1000, 80, 10, 50, 55, 10) };
        var stateHigh = new GameState { PlayerId = "test", EraId = "2024plus", Age = 11, Stats = new StatBlock(1000, 80, 10, 50, 85, 10) };

        // Act
        var resLow = ExamSystem.AllocateSecondarySchool(stateLow);
        var resMid = ExamSystem.AllocateSecondarySchool(stateMid);
        var resHigh = ExamSystem.AllocateSecondarySchool(stateHigh);

        // Assert
        Assert.Contains("第三組別", resLow, StringComparison.Ordinal);
        Assert.True(stateLow.HasFlag("school_band3"));

        Assert.Contains("第二組別", resMid, StringComparison.Ordinal);
        Assert.True(stateMid.HasFlag("school_band2"));

        Assert.Contains("第一組別", resHigh, StringComparison.Ordinal);
        Assert.True(stateHigh.HasFlag("school_band1"));
    }

    [Fact]
    public void RunSchoolLeavingExam_DSE_ShouldGenerateValidScores()
    {
        // Arrange
        var state = new GameState { PlayerId = "test", EraId = "2024plus", Age = 17, Stats = new StatBlock(1000, 80, 10, 50, 90, 10) };

        // Act
        var details = ExamSystem.RunSchoolLeavingExam(state, out var score);

        // Assert
        Assert.True(score >= 30 && score <= 42); // Outstanding score due to 90 education
        Assert.Contains("HKDSE", details, StringComparison.Ordinal);
        Assert.True(state.HasFlag("sat_dse"));
    }

    [Fact]
    public void RunSchoolLeavingExam_HKCEE_ShouldRequireScore14ToMatriculate()
    {
        // Arrange
        var stateLow = new GameState { PlayerId = "test", EraId = "2000s", Age = 17, Stats = new StatBlock(1000, 80, 10, 50, 15, 10) };
        var stateHigh = new GameState { PlayerId = "test", EraId = "2000s", Age = 17, Stats = new StatBlock(1000, 80, 10, 50, 95, 10) };

        // Act
        var detailsLow = ExamSystem.RunSchoolLeavingExam(stateLow, out var scoreLow);
        var detailsHigh = ExamSystem.RunSchoolLeavingExam(stateHigh, out var scoreHigh);

        // Assert
        Assert.True(scoreLow < 14);
        Assert.False(stateLow.HasFlag("matriculated"));

        Assert.True(scoreHigh >= 14);
        Assert.True(stateHigh.HasFlag("matriculated"));
    }

    [Fact]
    public void RunUniversityAdmission_DSE_EliteCourseThreshold()
    {
        // Arrange
        var state = new GameState { PlayerId = "test", EraId = "2024plus", Age = 18 };

        // Act
        var resElite = ExamSystem.RunUniversityAdmission(state, 35);
        var resNormal = ExamSystem.RunUniversityAdmission(state, 22);

        // Assert
        Assert.Contains("MBBS", resElite, StringComparison.Ordinal);
        Assert.Contains("香港中文大學", resNormal, StringComparison.Ordinal);
    }
}
