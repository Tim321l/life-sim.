using System.Text.Json.Serialization;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Persistence;

namespace HKLifeSim.Core.Data;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SaveEnvelope))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(StatBlock))]
[JsonSerializable(typeof(EraConfig))]
[JsonSerializable(typeof(LegacyRecord))]
[JsonSerializable(typeof(CharacterProfile))]
public sealed partial class HkJsonContext : JsonSerializerContext;
