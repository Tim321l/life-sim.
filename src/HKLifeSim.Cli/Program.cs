using System.Text;
using HKLifeSim.Core.Data;
using HKLifeSim.Core.Domain;
using HKLifeSim.Core.Events;
using HKLifeSim.Core.Systems;

Console.OutputEncoding = Encoding.UTF8;

var options = CliOptions.Parse(args);
var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

return options.ValidateContent
    ? Runner.RunValidateContent(dataDirectory)
    : Runner.RunLife(options, dataDirectory);

internal sealed class CliOptions
{
    public required string EraId { get; init; }

    public required int Seed { get; init; }

    public bool Auto { get; init; }

    public bool ValidateContent { get; init; }

    public required int Generations { get; init; }

    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var eraId = "2024plus";
        var seed = Environment.TickCount;
        var auto = false;
        var validateContent = false;
        var generations = 1;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--era":
                    eraId = args[++i];
                    break;
                case "--seed":
                    seed = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--auto":
                    auto = true;
                    break;
                case "--validate-content":
                    validateContent = true;
                    break;
                case "--generations":
                    generations = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }

        return new CliOptions
        {
            EraId = eraId,
            Seed = seed,
            Auto = auto,
            ValidateContent = validateContent,
            Generations = generations,
        };
    }
}

internal static class Runner
{
    public static int RunValidateContent(string dataDirectory)
    {
        try
        {
            var eras = LoadEras(dataDirectory);
            var totalEvents = eras.Sum(era => LoadEvents(dataDirectory, era).Count);
            Console.WriteLine($"OK: {totalEvents} events across {eras.Count} eras");
            return 0;
        }
        catch (EventDataException ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    public static int RunLife(CliOptions options, string dataDirectory)
    {
        var eras = LoadEras(dataDirectory);
        var era = eras.FirstOrDefault(e => e.EraId == options.EraId);
        if (era is null)
        {
            Console.WriteLine($"Unknown era '{options.EraId}'. Available: {string.Join(", ", eras.Select(e => e.EraId))}");
            return 1;
        }

        var events = LoadEvents(dataDirectory, era);
        var chain = new GenerationChain(eras);

        var generationNumber = 0;
        while (true)
        {
            generationNumber++;
            var seed = options.Seed + generationNumber - 1;
            var engine = new EventEngine(events, era, seed);
            var lifecycle = new LifecycleSystem(seed);
            var state = chain.StartNextGeneration(era, seed);

            Console.WriteLine();
            Console.WriteLine($"=== 第 {generationNumber} 代 Generation {generationNumber} ===");

            while (state.IsAlive)
            {
                var evt = engine.SelectNextEvent(state);
                PrintTurn(state, evt);

                var choiceId = options.Auto
                    ? engine.PickRandomChoiceId(evt)
                    : ReadChoiceFromConsole(evt);

                engine.ApplyChoice(state, evt, choiceId);

                if (!state.IsAlive)
                {
                    break;
                }

                lifecycle.AdvanceYear(state, era);
            }

            PrintObituary(state);

            var legacy = LegacySystem.GenerateLegacy(state);
            chain.Lineage.Add(legacy);
            PrintInheritanceSummary(legacy);

            if (options.Auto)
            {
                if (generationNumber >= options.Generations)
                {
                    break;
                }
            }
            else if (!PromptForNextGeneration())
            {
                break;
            }
        }

        return 0;
    }

    private static IReadOnlyList<EraConfig> LoadEras(string dataDirectory)
    {
        var json = File.ReadAllText(Path.Combine(dataDirectory, "eras.json"));
        return EraRepository.Load(json);
    }

    private static IReadOnlyList<GameEvent> LoadEvents(string dataDirectory, EraConfig era)
    {
        var files = era.EventPoolFiles.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(dataDirectory, f)));
        return EventRepository.Load(files, [era]);
    }

    private static void PrintTurn(GameState state, GameEvent evt)
    {
        Console.WriteLine();
        Console.WriteLine($"[{state.CurrentYear}] Age {state.Age} | Money {state.Stats.Money} Health {state.Stats.Health} Stress {state.Stats.Stress} FamilyBond {state.Stats.FamilyBond} Education {state.Stats.Education} Reputation {state.Stats.Reputation}");
        Console.WriteLine(evt.Title);
        Console.WriteLine(evt.Body);
        for (var i = 0; i < evt.Choices.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {evt.Choices[i].Text}");
        }
    }

    private static string ReadChoiceFromConsole(GameEvent evt)
    {
        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out var index) && index >= 1 && index <= evt.Choices.Count)
            {
                return evt.Choices[index - 1].Id;
            }

            Console.WriteLine("請輸入有效選項編號 / Enter a valid choice number.");
        }
    }

    private static bool PromptForNextGeneration()
    {
        Console.Write("開展下一代? (y/n) > ");
        var input = Console.ReadLine();
        return string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintObituary(GameState state)
    {
        Console.WriteLine();
        Console.WriteLine("=== 訃聞 Obituary ===");
        Console.WriteLine($"享年 (Age): {state.Age}");
        Console.WriteLine($"死因 (Cause): {state.DeathCause}");
        Console.WriteLine($"Money={state.Stats.Money} Health={state.Stats.Health} Stress={state.Stats.Stress} FamilyBond={state.Stats.FamilyBond} Education={state.Stats.Education} Reputation={state.Stats.Reputation}");
        Console.WriteLine($"Flags: {string.Join(", ", state.FlagsSet)}");
        Console.WriteLine($"Events experienced: {state.EventHistory.Count}");
    }

    private static void PrintInheritanceSummary(LegacyRecord legacy)
    {
        Console.WriteLine();
        Console.WriteLine("=== 家族傳承 Inheritance ===");
        Console.WriteLine($"遺產 (Inherited money, {legacy.SourceEraId} scale): {legacy.InheritedMoney}");
        Console.WriteLine($"聲望承傳 (Reputation carry-over): {legacy.FamilyReputationCarryOver}");
        Console.WriteLine($"傳承旗標 (Legacy flags): {string.Join(", ", legacy.InheritedFlags)}");
    }
}
