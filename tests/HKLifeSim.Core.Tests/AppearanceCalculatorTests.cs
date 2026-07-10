using FluentAssertions;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Presentation;

namespace HKLifeSim.Core.Tests;

public sealed class AppearanceCalculatorTests
{
    [Theory]
    [InlineData(0, Stage.Baby)]
    [InlineData(2, Stage.Baby)]
    [InlineData(3, Stage.Child)]
    [InlineData(11, Stage.Child)]
    [InlineData(12, Stage.Teen)]
    [InlineData(17, Stage.Teen)]
    [InlineData(18, Stage.Adult)]
    [InlineData(59, Stage.Adult)]
    [InlineData(60, Stage.Elder)]
    [InlineData(100, Stage.Elder)]
    public void Calculate_Maps_Age_To_The_Correct_Stage_At_Every_Boundary(int age, Stage expected)
    {
        var state = MakeState(age: age, stats: HealthyStats);

        var result = AppearanceCalculator.Calculate(state);

        result.Stage.Should().Be(expected);
    }

    [Fact]
    public void Calculate_Returns_Tombstone_And_Dead_When_Not_Alive_Regardless_Of_Age_Or_Stats()
    {
        var state = MakeState(age: 5, stats: HealthyStats);
        state.IsAlive = false;

        var result = AppearanceCalculator.Calculate(state);

        result.Stage.Should().Be(Stage.Tombstone);
        result.Mood.Should().Be(Mood.Dead);
    }

    [Fact]
    public void Calculate_Dead_Overrides_Sick_Stressed_And_Tired_All_At_Once()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 1, Stress: 99, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 0 });
        state.IsAlive = false;

        var result = AppearanceCalculator.Calculate(state);

        result.Mood.Should().Be(Mood.Dead);
    }

    [Fact]
    public void Calculate_Sick_Wins_Over_Stressed_When_Health_Below_Thirty_And_Stress_Above_Seventy()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 20, Stress: 90, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 50 });

        var result = AppearanceCalculator.Calculate(state);

        result.Mood.Should().Be(Mood.Sick);
    }

    [Fact]
    public void Calculate_Stressed_Wins_Over_Tired_When_Health_Is_Fine()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 80, Stress: 90, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 5 });

        var result = AppearanceCalculator.Calculate(state);

        result.Mood.Should().Be(Mood.Stressed);
    }

    [Fact]
    public void Calculate_Tired_Wins_Over_Idle_When_Stamina_Is_Low()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 5 });

        var result = AppearanceCalculator.Calculate(state);

        result.Mood.Should().Be(Mood.Tired);
    }

    [Fact]
    public void Calculate_Is_Idle_When_All_Stats_Are_Within_Normal_Ranges()
    {
        var state = MakeState(age: 30, stats: HealthyStats);

        var result = AppearanceCalculator.Calculate(state);

        result.Mood.Should().Be(Mood.Idle);
    }

    [Fact]
    public void Calculate_Health_At_Thirty_Is_Not_Sick()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 30, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 50 });

        AppearanceCalculator.Calculate(state).Mood.Should().NotBe(Mood.Sick);
    }

    [Fact]
    public void Calculate_Health_At_TwentyNine_Is_Sick()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 29, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 50 });

        AppearanceCalculator.Calculate(state).Mood.Should().Be(Mood.Sick);
    }

    [Fact]
    public void Calculate_Stress_At_Seventy_Is_Not_Stressed()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 80, Stress: 70, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 50 });

        AppearanceCalculator.Calculate(state).Mood.Should().NotBe(Mood.Stressed);
    }

    [Fact]
    public void Calculate_Stress_At_SeventyOne_Is_Stressed()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 80, Stress: 71, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 50 });

        AppearanceCalculator.Calculate(state).Mood.Should().Be(Mood.Stressed);
    }

    [Fact]
    public void Calculate_Stamina_At_Exactly_Twenty_Percent_Is_Not_Tired()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 10 });

        AppearanceCalculator.Calculate(state).Mood.Should().NotBe(Mood.Tired);
    }

    [Fact]
    public void Calculate_Stamina_Just_Below_Twenty_Percent_Is_Tired()
    {
        var state = MakeState(age: 30, stats: new StatBlock(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 9 });

        AppearanceCalculator.Calculate(state).Mood.Should().Be(Mood.Tired);
    }

    [Fact]
    public void Calculate_Throws_For_A_Null_State()
    {
        var act = () => AppearanceCalculator.Calculate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static StatBlock HealthyStats =>
        new(Money: 0, Health: 80, Stress: 10, FamilyBond: 50, Education: 50, Reputation: 50) { MaxStamina = 50, CurrentStamina = 50 };

    private static GameState MakeState(int age, StatBlock stats) => new()
    {
        PlayerId = "test",
        EraId = "2024plus",
        Age = age,
        Stats = stats,
    };
}
