using System.Text.Json.Serialization;
using HKLifeSim.Core.Activities;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Persistence;
using HKLifeSim.Core.Presentation;

namespace HKLifeSim.Core.Data;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SaveEnvelope))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(StatBlock))]
[JsonSerializable(typeof(StatDelta))]
[JsonSerializable(typeof(EraConfig))]
[JsonSerializable(typeof(LegacyRecord))]
[JsonSerializable(typeof(CharacterProfile))]
[JsonSerializable(typeof(GameEvent))]
[JsonSerializable(typeof(EventChoice))]
[JsonSerializable(typeof(EventCondition))]
[JsonSerializable(typeof(EraFile))]
[JsonSerializable(typeof(EventFile))]
[JsonSerializable(typeof(Activity))]
[JsonSerializable(typeof(ActivityFile))]
[JsonSerializable(typeof(SpriteManifest))]
[JsonSerializable(typeof(StageSheet))]
[JsonSerializable(typeof(PoseDef))]
[JsonSerializable(typeof(IconDef))]
[JsonSerializable(typeof(ActionDef))]
public sealed partial class HkJsonContext : JsonSerializerContext;
