using System.Text.Json;
using System.Text.Json.Serialization;

namespace HKLifeSim.Core.Domain;

// System.Text.Json's constructor-based deserialization for structs bypasses both C# parameter
// defaults and member initializers when a JSON property is absent (verified: a plain
// `int MaxStamina = 50` primary-constructor parameter, and later a separate `{ get; init; } = 50`
// property, both deserialized missing fields as 0). A custom converter is the only reliable way
// to give MaxStamina/CurrentStamina a real default of 50 for pre-stamina save files.
[JsonConverter(typeof(StatBlockJsonConverter))]
public readonly record struct StatBlock(
    int Money,
    int Health,
    int Stress,
    int FamilyBond,
    int Education,
    int Reputation)
{
    public static readonly IReadOnlyCollection<string> StatNames =
        ["Money", "Health", "Stress", "FamilyBond", "Education", "Reputation"];

    public int MaxStamina { get; init; } = 50;

    public int CurrentStamina { get; init; } = 50;

    public int GetStat(string statName) => statName switch
    {
        "Money" => Money,
        "Health" => Health,
        "Stress" => Stress,
        "FamilyBond" => FamilyBond,
        "Education" => Education,
        "Reputation" => Reputation,
        _ => throw new ArgumentException($"Unknown stat name '{statName}'.", nameof(statName)),
    };

    public static StatBlock CreateStarting(EraConfig era, LegacyRecord? legacy)
    {
        ArgumentNullException.ThrowIfNull(era);

        return new(
            Money: era.StartingMoney + (legacy?.InheritedMoney ?? 0),
            Health: 80,
            Stress: 10,
            FamilyBond: 50,
            Education: 10,
            Reputation: Clamp(10 + (legacy?.FamilyReputationCarryOver ?? 0)));
    }

    public StatBlock ApplyDelta(StatDelta delta) =>
        this with
        {
            Money = Money + delta.Money,
            Health = Clamp(Health + delta.Health),
            Stress = Clamp(Stress + delta.Stress),
            FamilyBond = Clamp(FamilyBond + delta.FamilyBond),
            Education = Clamp(Education + delta.Education),
            Reputation = Clamp(Reputation + delta.Reputation),
        };

    public StatBlock ResetStamina() => this with { CurrentStamina = MaxStamina };

    public bool IsFatal(out string cause)
    {
        if (Health <= 0)
        {
            cause = "health_zero";
            return true;
        }

        if (Stress >= 100)
        {
            cause = "stress_breakdown";
            return true;
        }

        cause = string.Empty;
        return false;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

internal sealed class StatBlockJsonConverter : JsonConverter<StatBlock>
{
    public override StatBlock Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a JSON object for StatBlock.");
        }

        int money = 0, health = 0, stress = 0, familyBond = 0, education = 0, reputation = 0;
        var maxStamina = 50;
        var currentStamina = 50;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "money": money = reader.GetInt32(); break;
                case "health": health = reader.GetInt32(); break;
                case "stress": stress = reader.GetInt32(); break;
                case "familyBond": familyBond = reader.GetInt32(); break;
                case "education": education = reader.GetInt32(); break;
                case "reputation": reputation = reader.GetInt32(); break;
                case "maxStamina": maxStamina = reader.GetInt32(); break;
                case "currentStamina": currentStamina = reader.GetInt32(); break;
                default: reader.Skip(); break;
            }
        }

        return new StatBlock(money, health, stress, familyBond, education, reputation)
        {
            MaxStamina = maxStamina,
            CurrentStamina = currentStamina,
        };
    }

    public override void Write(Utf8JsonWriter writer, StatBlock value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("money", value.Money);
        writer.WriteNumber("health", value.Health);
        writer.WriteNumber("stress", value.Stress);
        writer.WriteNumber("familyBond", value.FamilyBond);
        writer.WriteNumber("education", value.Education);
        writer.WriteNumber("reputation", value.Reputation);
        writer.WriteNumber("maxStamina", value.MaxStamina);
        writer.WriteNumber("currentStamina", value.CurrentStamina);
        writer.WriteEndObject();
    }
}
